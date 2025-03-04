﻿using Latios.Myri.Driver;
using Latios.Transforms.Abstract;
using Unity.Audio;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

using static Unity.Entities.SystemAPI;

namespace Latios.Myri.Systems
{
	[DisableAutoCreation]
	[UpdateInGroup(typeof(Latios.Systems.PreSyncPointGroup))]
	[BurstCompile]
	public partial struct AudioSystem : ISystem, ISystemShouldUpdate
	{
		bool m_initialized;

		private DSPGraph m_graph;
		private int      m_driverKey;
		private int      m_sampleRate;
		private int      m_samplesPerFrame;

		private DSPNode              m_mixNode;
		private DSPConnection        m_mixToLimiterMasterConnection;
		private NativeList<int>      m_mixNodePortFreelist;
		private NativeReference<int> m_mixNodePortCount;

		private DSPNode       m_limiterMasterNode;
		private DSPConnection m_limiterMasterToOutputConnection;

		private DSPNode                                     m_ildNode;
		private NativeReference<int>                        m_ildNodePortCount;
		private NativeReference<long>                       m_packedFrameCounterBufferId;  //MSB bufferId, LSB frame
		private NativeReference<int>                        m_audioFrame;
		private NativeReference<int>                        m_lastPlayedAudioFrame;
		private NativeReference<int>                        m_lastReadBufferId;
		private int                                         m_currentBufferId;
		private NativeList<OwnedIldBuffer>                  m_buffersInFlight;
		private NativeQueue<AudioFrameBufferHistoryElement> m_audioFrameHistory;
		private NativeList<ListenerGraphState>              m_listenerGraphStatesToDispose;

		private JobHandle	m_lastUpdateJobHandle;
		private EntityQuery	m_aliveListenersQuery;
		private EntityQuery	m_deadListenersQuery;
		private EntityQuery	m_oneshotsToDestroyWhenFinishedQuery;
		private EntityQuery	m_oneshotsQuery;
		private EntityQuery	m_loopedQuery;
		private EntityQuery	m_filteredQuery;

		ComponentTypeHandle<AudioListener>				m_listenerHandle;
		ComponentTypeHandle<AudioSourceOneShot>			m_oneshotHandle;
		ComponentTypeHandle<AudioSourceLooped>			m_loopedHandle;
		ComponentTypeHandle<AudioSourceFilter>			m_filteredHandle;
		BufferTypeHandle<AudioSourceFilterBufferInput>	m_bufferHandle;
		ComponentTypeHandle<AudioSourceEmitterCone>		m_coneHandle;
		WorldTransformReadOnlyAspect.TypeHandle			m_worldTransformHandle;

		LatiosWorldUnmanaged	latiosWorld;

		public void OnCreate(ref SystemState state)
		{
			latiosWorld = state.GetLatiosWorldUnmanaged();

			m_initialized = false;

			latiosWorld.worldBlackboardEntity.AddComponentDataIfMissing(new AudioSettings
			{
				safetyAudioFrames             = 2,
				audioFramesPerUpdate          = 1,
				lookaheadAudioFrames          = 0,
				logWarningIfBuffersAreStarved = false
			});

			// Create queries
			m_aliveListenersQuery					= state.Fluent().With<AudioListener>(true).Build();
			m_deadListenersQuery					= state.Fluent().Without<AudioListener>().With<ListenerGraphState>().Build();
			m_oneshotsToDestroyWhenFinishedQuery	= state.Fluent().With<AudioSourceOneShot>().With<AudioSourceDestroyOneShotWhenFinished>(true).Build();
			m_oneshotsQuery							= state.Fluent().With<AudioSourceOneShot>().Build();
			m_loopedQuery							= state.Fluent().With<AudioSourceLooped>().Build();
			m_filteredQuery							= state.Fluent().With<AudioSourceFilter>().With<AudioSourceFilterBufferInput>().Build();

			m_listenerHandle		= state.GetComponentTypeHandle<AudioListener>(true);
			m_oneshotHandle			= state.GetComponentTypeHandle<AudioSourceOneShot>(false);
			m_loopedHandle			= state.GetComponentTypeHandle<AudioSourceLooped>(false);
			m_filteredHandle		= state.GetComponentTypeHandle<AudioSourceFilter>(false);
			m_bufferHandle			= state.GetBufferTypeHandle<AudioSourceFilterBufferInput>(true);
			m_coneHandle			= state.GetComponentTypeHandle<AudioSourceEmitterCone>(true);
			m_worldTransformHandle	= new WorldTransformReadOnlyAspect.TypeHandle(ref state);
		}

		public bool ShouldUpdateSystem(ref SystemState state)
		{
			if (m_initialized)
			{
				DriverManager.Update();
				return true;
			}

			if (m_aliveListenersQuery.IsEmptyIgnoreFilter && m_deadListenersQuery.IsEmptyIgnoreFilter && m_loopedQuery.IsEmptyIgnoreFilter && m_oneshotsQuery.IsEmptyIgnoreFilter &&
				m_oneshotsToDestroyWhenFinishedQuery.IsEmptyIgnoreFilter && m_filteredQuery.IsEmptyIgnoreFilter)
				return false;

			m_initialized = true;

			// Initialize containers first
			m_mixNodePortFreelist          = new NativeList<int>(Allocator.Persistent);
			m_mixNodePortCount             = new NativeReference<int>(Allocator.Persistent);
			m_ildNodePortCount             = new NativeReference<int>(Allocator.Persistent);
			m_packedFrameCounterBufferId   = new NativeReference<long>(Allocator.Persistent);
			m_audioFrame                   = new NativeReference<int>(Allocator.Persistent);
			m_lastPlayedAudioFrame         = new NativeReference<int>(Allocator.Persistent);
			m_lastReadBufferId             = new NativeReference<int>(Allocator.Persistent);
			m_buffersInFlight              = new NativeList<OwnedIldBuffer>(Allocator.Persistent);
			m_audioFrameHistory            = new NativeQueue<AudioFrameBufferHistoryElement>(Allocator.Persistent);
			m_listenerGraphStatesToDispose = new NativeList<ListenerGraphState>(Allocator.Persistent);

			// Create graph and driver
			var format   = ChannelEnumConverter.GetSoundFormatFromSpeakerMode(UnityEngine.AudioSettings.speakerMode);
			var channels = ChannelEnumConverter.GetChannelCountFromSoundFormat(format);
			UnityEngine.AudioSettings.GetDSPBufferSize(out m_samplesPerFrame, out _);
			m_sampleRate = UnityEngine.AudioSettings.outputSampleRate;
			m_graph      = DSPGraph.Create(format, channels, m_samplesPerFrame, m_sampleRate);
			m_driverKey  = DriverManager.RegisterGraph(ref m_graph);

			var commandBlock = m_graph.CreateCommandBlock();
			m_mixNode        = commandBlock.CreateDSPNode<MixStereoPortsNode.Parameters, MixStereoPortsNode.SampleProviders, MixStereoPortsNode>();
			commandBlock.AddOutletPort(m_mixNode, 2);
			m_limiterMasterNode = commandBlock.CreateDSPNode<BrickwallLimiterNode.Parameters, BrickwallLimiterNode.SampleProviders, BrickwallLimiterNode>();
			commandBlock.AddInletPort(m_limiterMasterNode, 2);
			commandBlock.AddOutletPort(m_limiterMasterNode, 2);
			m_mixToLimiterMasterConnection    = commandBlock.Connect(m_mixNode, 0, m_limiterMasterNode, 0);
			m_limiterMasterToOutputConnection = commandBlock.Connect(m_limiterMasterNode, 0, m_graph.RootDSP, 0);
			m_ildNode                         = commandBlock.CreateDSPNode<ReadIldBuffersNode.Parameters, ReadIldBuffersNode.SampleProviders, ReadIldBuffersNode>();
			unsafe
			{
				commandBlock.UpdateAudioKernel<SetReadIldBuffersNodePackedFrameBufferId, ReadIldBuffersNode.Parameters, ReadIldBuffersNode.SampleProviders, ReadIldBuffersNode>(
					new SetReadIldBuffersNodePackedFrameBufferId { ptr = (long*)m_packedFrameCounterBufferId.GetUnsafePtr() },
					m_ildNode);
			}
			commandBlock.Complete();

			// Force initialization of Burst
			commandBlock  = m_graph.CreateCommandBlock();
			var dummyNode = commandBlock.CreateDSPNode<MixPortsToStereoNode.Parameters, MixPortsToStereoNode.SampleProviders, MixPortsToStereoNode>();
			StateVariableFilterNode.Create(commandBlock, StateVariableFilterNode.FilterType.Bandpass, 0f, 0f, 0f, 1);
			commandBlock.UpdateAudioKernel<MixPortsToStereoNodeUpdate, MixPortsToStereoNode.Parameters, MixPortsToStereoNode.SampleProviders, MixPortsToStereoNode>(
				new MixPortsToStereoNodeUpdate { leftChannelCount = 0 },
				dummyNode);
			commandBlock.UpdateAudioKernel<ReadIldBuffersNodeUpdate, ReadIldBuffersNode.Parameters, ReadIldBuffersNode.SampleProviders, ReadIldBuffersNode>(new ReadIldBuffersNodeUpdate
			{
				ildBuffer = new IldBuffer(),
			},
																																							m_ildNode);
			commandBlock.Cancel();

			DriverManager.Update();
			return true;
		}

		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			var ecsJH = state.Dependency;

			//Query arrays
			var aliveListenerEntities = m_aliveListenersQuery.ToEntityArray(Allocator.TempJob);
			var deadListenerEntities  = m_deadListenersQuery.ToEntityArray(Allocator.TempJob);

			//Type handles
			m_listenerHandle.Update(ref state);
			m_oneshotHandle.Update(ref state);
			m_loopedHandle.Update(ref state);
			m_filteredHandle.Update(ref state);
			m_bufferHandle.Update(ref state);
			m_coneHandle.Update(ref state);
			m_worldTransformHandle.Update(ref state);

			var audioSettingsLookup          = GetComponentLookup<AudioSettings>(true);
			var listenerLookup               = GetComponentLookup<AudioListener>(true);
			var listenerGraphStateLookup     = GetComponentLookup<ListenerGraphState>(false);
			var entityOutputGraphStateLookup = GetComponentLookup<EntityOutputGraphState>(false);

			//Buffer
			m_currentBufferId++;
			var ildBuffer = new OwnedIldBuffer
			{
				buffer   = new NativeList<float>(Allocator.Persistent),
				channels = new NativeList<IldBufferChannel>(Allocator.Persistent),
				bufferId = m_currentBufferId
			};

			AudioSettings	audioSettings = audioSettingsLookup[latiosWorld.worldBlackboardEntity];

			//Containers
			var entityCommandBuffer      = latiosWorld.syncPoint.CreateEntityCommandBuffer();
			var dspCommandBlock          = m_graph.CreateCommandBlock();
			var listenersWithTransforms  = new NativeList<ListenerWithTransform>(aliveListenerEntities.Length, Allocator.TempJob);
			var listenerBufferParameters = new NativeArray<ListenerBufferParameters>(aliveListenerEntities.Length,
																					 Allocator.TempJob,
																					 NativeArrayOptions.UninitializedMemory);
			var forIndexToListenerAndChannelIndices	= new NativeList<int2>(Allocator.TempJob);
			var oneshotEmitters						= new NativeArray<OneshotEmitter>(m_oneshotsQuery.CalculateEntityCount(),
																					  Allocator.TempJob,
																					  NativeArrayOptions.UninitializedMemory);
			var loopedEmitters						= new NativeArray<LoopedEmitter>(m_loopedQuery.CalculateEntityCount(), Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
			var filteredEmitters					= new NativeArray<FilterEmitter>(m_filteredQuery.CalculateEntityCount(), Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
			var oneshotWeightsStream				= new NativeStream(oneshotEmitters.Length / CullingAndWeighting.kBatchSize + 1, Allocator.TempJob);
			var loopedWeightsStream					= new NativeStream(loopedEmitters.Length / CullingAndWeighting.kBatchSize + 1, Allocator.TempJob);
			var filteredWeightsStream				= new NativeStream(filteredEmitters.Length / CullingAndWeighting.kBatchSize + 1, Allocator.TempJob);
			var oneshotListenerEmitterPairsStream	= new NativeStream(oneshotEmitters.Length / CullingAndWeighting.kBatchSize + 1, Allocator.TempJob);
			var loopedListenerEmitterPairsStream	= new NativeStream(loopedEmitters.Length / CullingAndWeighting.kBatchSize + 1, Allocator.TempJob);
			var filteredListenerEmitterPairsStream	= new NativeStream(filteredEmitters.Length / CullingAndWeighting.kBatchSize + 1, Allocator.TempJob);
			var oneshotClipFrameLookups				= new NativeList<ClipFrameLookup>(Allocator.TempJob);
			var loopedClipFrameLookups				= new NativeList<ClipFrameLookup>(Allocator.TempJob);
			var filteredFrameLookups				= new NativeList<BufferFrameLookup>(Allocator.TempJob);
			var oneshotBatchedWeights				= new NativeList<Weights>(Allocator.TempJob);
			var loopedBatchedWeights				= new NativeList<Weights>(Allocator.TempJob);
			var filteredBatchedWeights				= new NativeList<Weights>(Allocator.TempJob);
			var oneshotTargetListenerIndices		= new NativeList<int>(Allocator.TempJob);
			var loopedTargetListenerIndices			= new NativeList<int>(Allocator.TempJob);
			var filteredTargetListenerIndices		= new NativeList<int>(Allocator.TempJob);
			NativeArray<float>	filterBuffers		= new(m_samplesPerFrame * m_filteredQuery.CalculateEntityCount() * (audioSettings.audioFramesPerUpdate + audioSettings.safetyAudioFrames) * 2, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

			//Jobs
			m_lastUpdateJobHandle.Complete();

			//This may lag behind what the job threads will see.
			//That's fine, as this is only used for disposing memory.
			int lastReadIldBufferFromMainThread = m_lastReadBufferId.Value;

			var captureListenersJH = new InitUpdateDestroy.UpdateListenersJob
			{
				listenerHandle          = m_listenerHandle,
				worldTransformHandle    = m_worldTransformHandle,
				listenersWithTransforms = listenersWithTransforms
			}.Schedule(m_aliveListenersQuery, ecsJH);

			var captureFrameJH = new GraphHandling.CaptureIldFrameJob
			{
				packedFrameCounterBufferId = m_packedFrameCounterBufferId,
				audioFrame                 = m_audioFrame,
				lastPlayedAudioFrame       = m_lastPlayedAudioFrame,
				lastReadBufferId           = m_lastReadBufferId,
				audioFrameHistory          = m_audioFrameHistory,
				audioSettingsLookup        = audioSettingsLookup,
				worldBlackboardEntity      = latiosWorld.worldBlackboardEntity
			}.Schedule();

			var ecsCaptureFrameJH = JobHandle.CombineDependencies(ecsJH, captureFrameJH);

			var updateListenersGraphJH = new GraphHandling.UpdateListenersGraphJob
			{
				listenerEntities                    = aliveListenerEntities,
				destroyedListenerEntities           = deadListenerEntities,
				listenerLookup                      = listenerLookup,
				listenerGraphStateLookup            = listenerGraphStateLookup,
				listenerOutputGraphStateLookup      = entityOutputGraphStateLookup,
				ecb                                 = entityCommandBuffer,
				statesToDisposeThisFrame            = m_listenerGraphStatesToDispose,
				audioSettingsLookup                 = audioSettingsLookup,
				worldBlackboardEntity               = latiosWorld.worldBlackboardEntity,
				audioFrame                          = m_audioFrame,
				audioFrameHistory                   = m_audioFrameHistory,
				systemMixNodePortFreelist           = m_mixNodePortFreelist,
				systemMixNodePortCount              = m_mixNodePortCount,
				systemMixNode                       = m_mixNode,
				systemIldNodePortCount              = m_ildNodePortCount,
				systemIldNode                       = m_ildNode,
				commandBlock                        = dspCommandBlock,
				listenerBufferParameters            = listenerBufferParameters,
				forIndexToListenerAndChannelIndices = forIndexToListenerAndChannelIndices,
				outputSamplesMegaBuffer             = ildBuffer.buffer,
				outputSamplesMegaBufferChannels     = ildBuffer.channels,
				bufferId                            = m_currentBufferId,
				samplesPerFrame                     = m_samplesPerFrame
			}.Schedule(JobHandle.CombineDependencies(captureListenersJH, captureFrameJH));

			var destroyOneshotsJH = new InitUpdateDestroy.DestroyOneshotsWhenFinishedJob
			{
				expireHandle          = GetComponentTypeHandle<AudioSourceDestroyOneShotWhenFinished>(false),
				oneshotHandle         = m_oneshotHandle,
				audioFrame            = m_audioFrame,
				lastPlayedAudioFrame  = m_lastPlayedAudioFrame,
				sampleRate            = m_sampleRate,
				settingsLookup        = audioSettingsLookup,
				samplesPerFrame       = m_samplesPerFrame,
				worldBlackboardEntity = latiosWorld.worldBlackboardEntity
			}.ScheduleParallel(m_oneshotsToDestroyWhenFinishedQuery, ecsCaptureFrameJH);

			var firstEntityInChunkIndices = m_oneshotsQuery.CalculateBaseEntityIndexArrayAsync(Allocator.TempJob, destroyOneshotsJH, out var updateOneshotsJH);

			updateOneshotsJH = new InitUpdateDestroy.UpdateOneshotsJob
			{
				oneshotHandle             = m_oneshotHandle,
				worldTransformHandle      = m_worldTransformHandle,
				coneHandle                = m_coneHandle,
				audioFrame                = m_audioFrame,
				lastPlayedAudioFrame      = m_lastPlayedAudioFrame,
				lastConsumedBufferId      = m_lastReadBufferId,
				bufferId                  = m_currentBufferId,
				emitters                  = oneshotEmitters,
				firstEntityInChunkIndices = firstEntityInChunkIndices
			}.ScheduleParallel(m_oneshotsQuery, updateOneshotsJH);

			firstEntityInChunkIndices = m_loopedQuery.CalculateBaseEntityIndexArrayAsync(Allocator.TempJob, ecsCaptureFrameJH, out var updateLoopedJH);

			updateLoopedJH = new InitUpdateDestroy.UpdateLoopedJob
			{
				loopedHandle              = m_loopedHandle,
				worldTransformHandle      = m_worldTransformHandle,
				coneHandle                = m_coneHandle,
				audioFrame                = m_audioFrame,
				lastConsumedBufferId      = m_lastReadBufferId,
				bufferId                  = m_currentBufferId,
				sampleRate                = m_sampleRate,
				samplesPerFrame           = m_samplesPerFrame,
				emitters                  = loopedEmitters,
				firstEntityInChunkIndices = firstEntityInChunkIndices
			}.ScheduleParallel(m_loopedQuery, updateLoopedJH);

			firstEntityInChunkIndices = m_filteredQuery.CalculateBaseEntityIndexArrayAsync(Allocator.TempJob, ecsCaptureFrameJH, out var updateFilteredJH);

			updateFilteredJH = new InitUpdateDestroy.UpdateFilterJob {
				bufferHandle = m_bufferHandle,
				filteredHandle = m_filteredHandle,
				worldTransformHandle = m_worldTransformHandle,
				coneHandle = m_coneHandle,
				audioFrame = m_audioFrame,
				lastPlayedAudioFrame = m_lastPlayedAudioFrame,
				lastConsumedBufferId = m_lastReadBufferId,
				bufferId = m_currentBufferId,
				emitters = filteredEmitters,
				firstEntityInChunkIndices = firstEntityInChunkIndices,
				filterBuffers = filterBuffers,
			}.ScheduleParallel(m_filteredQuery, updateFilteredJH);

			//No more ECS
			var oneshotsCullingWeightingJH = new CullingAndWeighting.OneshotsJob
			{
				emitters                = oneshotEmitters,
				listenersWithTransforms = listenersWithTransforms,
				weights                 = oneshotWeightsStream.AsWriter(),
				listenerEmitterPairs    = oneshotListenerEmitterPairsStream.AsWriter()
			}.ScheduleBatch(oneshotEmitters.Length, CullingAndWeighting.kBatchSize, JobHandle.CombineDependencies(captureListenersJH, updateOneshotsJH));

			var loopedCullingWeightingJH = new CullingAndWeighting.LoopedJob
			{
				emitters                = loopedEmitters,
				listenersWithTransforms = listenersWithTransforms,
				weights                 = loopedWeightsStream.AsWriter(),
				listenerEmitterPairs    = loopedListenerEmitterPairsStream.AsWriter()
			}.ScheduleBatch(loopedEmitters.Length, CullingAndWeighting.kBatchSize, JobHandle.CombineDependencies(captureListenersJH, updateLoopedJH));

			var filteredCullingWeightingJH = new CullingAndWeighting.FilterJob {
				emitters = filteredEmitters,
				listenersWithTransforms = listenersWithTransforms,
				weights = filteredWeightsStream.AsWriter(),
				listenerEmitterPairs = filteredListenerEmitterPairsStream.AsWriter()
			}.ScheduleBatch(filteredEmitters.Length, CullingAndWeighting.kBatchSize, JobHandle.CombineDependencies(captureListenersJH, updateFilteredJH));

			var oneshotsBatchingJH = new Batching.BatchOneshotsJob
			{
				emitters              = oneshotEmitters,
				pairWeights           = oneshotWeightsStream.AsReader(),
				listenerEmitterPairs  = oneshotListenerEmitterPairsStream.AsReader(),
				clipFrameLookups      = oneshotClipFrameLookups,
				batchedWeights        = oneshotBatchedWeights,
				targetListenerIndices = oneshotTargetListenerIndices
			}.Schedule(oneshotsCullingWeightingJH);

			var loopedBatchingJH = new Batching.BatchLoopedJob
			{
				emitters              = loopedEmitters,
				pairWeights           = loopedWeightsStream.AsReader(),
				listenerEmitterPairs  = loopedListenerEmitterPairsStream.AsReader(),
				clipFrameLookups      = loopedClipFrameLookups,
				batchedWeights        = loopedBatchedWeights,
				targetListenerIndices = loopedTargetListenerIndices
			}.Schedule(loopedCullingWeightingJH);

			var filteredBatchingJH = new Batching.BatchFilterJob {
				emitters = filteredEmitters,
				pairWeights = filteredWeightsStream.AsReader(),
				listenerEmitterPairs = filteredListenerEmitterPairsStream.AsReader(),
				clipFrameLookups = filteredFrameLookups,
				batchedWeights = filteredBatchedWeights,
				targetListenerIndices = filteredTargetListenerIndices
			}.Schedule(filteredCullingWeightingJH);

			var oneshotSamplingJH = new Sampling.SampleOneshotClipsJob
			{
				clipFrameLookups                    = oneshotClipFrameLookups.AsDeferredJobArray(),
				weights                             = oneshotBatchedWeights.AsDeferredJobArray(),
				targetListenerIndices               = oneshotTargetListenerIndices.AsDeferredJobArray(),
				listenerBufferParameters            = listenerBufferParameters,
				forIndexToListenerAndChannelIndices = forIndexToListenerAndChannelIndices.AsDeferredJobArray(),
				outputSamplesMegaBuffer             = ildBuffer.buffer.AsDeferredJobArray(),
				sampleRate                          = m_sampleRate,
				samplesPerFrame                     = m_samplesPerFrame,
				audioFrame                          = m_audioFrame
			}.Schedule(forIndexToListenerAndChannelIndices, 1, JobHandle.CombineDependencies(updateListenersGraphJH, oneshotsBatchingJH));

			var loopedSamplingJH = new Sampling.SampleLoopedClipsJob
			{
				clipFrameLookups                    = loopedClipFrameLookups.AsDeferredJobArray(),
				weights                             = loopedBatchedWeights.AsDeferredJobArray(),
				targetListenerIndices               = loopedTargetListenerIndices.AsDeferredJobArray(),
				listenerBufferParameters            = listenerBufferParameters,
				forIndexToListenerAndChannelIndices = forIndexToListenerAndChannelIndices.AsDeferredJobArray(),
				outputSamplesMegaBuffer             = ildBuffer.buffer.AsDeferredJobArray(),
				sampleRate                          = m_sampleRate,
				samplesPerFrame                     = m_samplesPerFrame,
				audioFrame                          = m_audioFrame
			}.Schedule(forIndexToListenerAndChannelIndices, 1, JobHandle.CombineDependencies(oneshotSamplingJH, loopedBatchingJH));

			var filteredSamplingJH = new Sampling.SampleFilterBufferJob {
				clipFrameLookups = filteredFrameLookups.AsDeferredJobArray(),
				weights = filteredBatchedWeights.AsDeferredJobArray(),
				targetListenerIndices = filteredTargetListenerIndices.AsDeferredJobArray(),
				listenerBufferParameters = listenerBufferParameters,
				forIndexToListenerAndChannelIndices = forIndexToListenerAndChannelIndices.AsDeferredJobArray(),
				outputSamplesMegaBuffer = ildBuffer.buffer.AsDeferredJobArray(),
				targetFrame = m_audioFrame,
				sampleRate = m_sampleRate,
				samplesPerFrame = m_samplesPerFrame,
				filterBuffers = filterBuffers
			}.Schedule(forIndexToListenerAndChannelIndices, 1, JobHandle.CombineDependencies(loopedSamplingJH, filteredBatchingJH));

			var shipItJH = new GraphHandling.SubmitToDspGraphJob
			{
				commandBlock = dspCommandBlock
			}.Schedule(filteredSamplingJH);

			state.Dependency = JobHandle.CombineDependencies(updateListenersGraphJH,  //handles captureListener and captureFrame
															 updateOneshotsJH,  //handles destroyOneshots
															 JobHandle.CombineDependencies(updateLoopedJH, updateFilteredJH)
															 );

			var disposeJobHandles = new NativeList<JobHandle>(Allocator.TempJob) {
				aliveListenerEntities.Dispose(updateListenersGraphJH),
				deadListenerEntities.Dispose(updateListenersGraphJH),
				listenersWithTransforms.Dispose(JobHandle.CombineDependencies(oneshotsCullingWeightingJH, loopedCullingWeightingJH, filteredCullingWeightingJH)),
				listenerBufferParameters.Dispose(filteredSamplingJH),
				forIndexToListenerAndChannelIndices.Dispose(filteredSamplingJH),
				oneshotEmitters.Dispose(oneshotsBatchingJH),
				loopedEmitters.Dispose(loopedBatchingJH),
				filteredEmitters.Dispose(filteredBatchingJH),
				filterBuffers.Dispose(filteredSamplingJH),
				oneshotWeightsStream.Dispose(oneshotsBatchingJH),
				loopedWeightsStream.Dispose(loopedBatchingJH),
				filteredWeightsStream.Dispose(filteredBatchingJH),
				oneshotListenerEmitterPairsStream.Dispose(oneshotsBatchingJH),
				loopedListenerEmitterPairsStream.Dispose(loopedBatchingJH),
				filteredListenerEmitterPairsStream.Dispose(filteredBatchingJH),
				oneshotClipFrameLookups.Dispose(oneshotSamplingJH),
				loopedClipFrameLookups.Dispose(loopedSamplingJH),
				filteredFrameLookups.Dispose(filteredSamplingJH),
				oneshotBatchedWeights.Dispose(oneshotSamplingJH),
				loopedBatchedWeights.Dispose(loopedSamplingJH),
				filteredBatchedWeights.Dispose(filteredSamplingJH),
				oneshotTargetListenerIndices.Dispose(oneshotSamplingJH),
				loopedTargetListenerIndices.Dispose(loopedSamplingJH),
				filteredTargetListenerIndices.Dispose(filteredSamplingJH),
				shipItJH
			};

			for (int i = 0; i < m_buffersInFlight.Length; i++)
			{
				var buffer = m_buffersInFlight[i];
				if (buffer.bufferId - lastReadIldBufferFromMainThread < 0)
				{
					disposeJobHandles.Add(buffer.buffer.Dispose(ecsJH));
					disposeJobHandles.Add(buffer.channels.Dispose(ecsJH));
					m_buffersInFlight.RemoveAtSwapBack(i);
					i--;
				}
			}

			m_lastUpdateJobHandle = JobHandle.CombineDependencies(disposeJobHandles.AsArray());
			disposeJobHandles.Dispose();

			m_buffersInFlight.Add(ildBuffer);
		}

		public void OnDestroy(ref SystemState state)
		{
			if (!m_initialized)
				return;

			//UnityEngine.Debug.Log("AudioSystem.OnDestroy");
			m_lastUpdateJobHandle.Complete();
			state.CompleteDependency();
			var commandBlock = m_graph.CreateCommandBlock();
			foreach (var s in m_listenerGraphStatesToDispose)
			{
				foreach (var c in s.ildConnections)
					commandBlock.Disconnect(c.connection);
				foreach (var c in s.connections)
					commandBlock.Disconnect(c);
				foreach (var n in s.nodes)
					commandBlock.ReleaseDSPNode(n);
				s.nodes.Dispose();
				s.ildConnections.Dispose();
				s.connections.Dispose();
			}
			commandBlock.Disconnect(m_mixToLimiterMasterConnection);
			commandBlock.Disconnect(m_limiterMasterToOutputConnection);
			commandBlock.ReleaseDSPNode(m_ildNode);
			commandBlock.ReleaseDSPNode(m_mixNode);
			commandBlock.ReleaseDSPNode(m_limiterMasterNode);
			commandBlock.Complete();
			DriverManager.DeregisterAndDisposeGraph(m_driverKey);

			m_lastUpdateJobHandle.Complete();
			m_mixNodePortFreelist.Dispose();
			m_mixNodePortCount.Dispose();
			m_ildNodePortCount.Dispose();
			m_packedFrameCounterBufferId.Dispose();
			m_audioFrame.Dispose();
			m_lastPlayedAudioFrame.Dispose();
			m_lastReadBufferId.Dispose();
			m_audioFrameHistory.Dispose();
			m_listenerGraphStatesToDispose.Dispose();

			foreach (var buffer in m_buffersInFlight)
			{
				buffer.buffer.Dispose();
				buffer.channels.Dispose();
			}
			m_buffersInFlight.Dispose();
		}

		private struct OwnedIldBuffer
		{
			public NativeList<float>            buffer;
			public NativeList<IldBufferChannel> channels;
			public int                          bufferId;
		}
	}
}

