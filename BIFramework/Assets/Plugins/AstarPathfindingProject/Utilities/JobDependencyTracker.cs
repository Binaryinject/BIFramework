//#define DEBUG_JOBS
namespace Pathfinding.Jobs {
	using System.Reflection;
	using Unity.Collections;
	using Unity.Jobs;
	using System.Collections.Generic;
	using Unity.Collections.LowLevel.Unsafe;
	using Pathfinding.Util;
	using System.Runtime.InteropServices;

	/// <summary>
	/// Disable the check that prevents jobs from including uninitialized native arrays open for reading.
	///
	/// Sometimes jobs have to include a readable native array that starts out uninitialized.
	/// The job might for example write to it and later read from it in the same job.
	///
	/// See: <see cref="JobDependencyTracker.NewNativeArray"/>
	/// </summary>
	class DisableUninitializedReadCheckAttribute : System.Attribute {
	}

	public interface IArenaDisposable {
		void DisposeWith(DisposeArena arena);
	}

	/// <summary>Convenient collection of items that can be disposed together</summary>
	public class DisposeArena {
		List<NativeArray<byte> > buffer;
		List<NativeList<byte> > buffer2;
		List<NativeQueue<byte> > buffer3;
		List<GCHandle> gcHandles;

		public void Add<T>(NativeArray<T> data) where T : unmanaged {
			if (buffer == null) buffer = ListPool<NativeArray<byte> >.Claim();
			buffer.Add(data.Reinterpret<byte>(UnsafeUtility.SizeOf<T>()));
		}

		public void Add<T>(NativeList<T> data) where T : unmanaged {
			// SAFETY: This is safe because NativeList<byte> and NativeList<T> have the same memory layout.
			var byteList = Unity.Collections.LowLevel.Unsafe.UnsafeUtility.As<NativeList<T>, NativeList<byte> >(ref data);
			if (buffer2 == null) buffer2 = ListPool<NativeList<byte> >.Claim();
			buffer2.Add(byteList);
		}

		public void Add<T>(NativeQueue<T> data) where T : unmanaged {
			// SAFETY: This is safe because NativeQueue<byte> and NativeQueue<T> have the same memory layout.
			var byteList = Unity.Collections.LowLevel.Unsafe.UnsafeUtility.As<NativeQueue<T>, NativeQueue<byte> >(ref data);
			if (buffer3 == null) buffer3 = ListPool<NativeQueue<byte> >.Claim();
			buffer3.Add(byteList);
		}

		public void Add<T>(T data) where T : IArenaDisposable {
			data.DisposeWith(this);
		}

		public void Add (GCHandle handle) {
			if (gcHandles == null) gcHandles = ListPool<GCHandle>.Claim();
			gcHandles.Add(handle);
		}

		/// <summary>
		/// Dispose all items in the arena.
		/// This also clears the arena and makes it available for reuse.
		/// </summary>
		public void DisposeAll () {
			UnityEngine.Profiling.Profiler.BeginSample("Disposing");
			if (buffer != null) {
				for (int i = 0; i < buffer.Count; i++) buffer[i].Dispose();
				ListPool<NativeArray<byte> >.Release(ref buffer);
			}
			if (buffer2 != null) {
				for (int i = 0; i < buffer2.Count; i++) buffer2[i].Dispose();
				ListPool<NativeList<byte> >.Release(ref buffer2);
			}
			if (buffer3 != null) {
				for (int i = 0; i < buffer3.Count; i++) buffer3[i].Dispose();
				ListPool<NativeQueue<byte> >.Release(ref buffer3);
			}
			if (gcHandles != null) {
				for (int i = 0; i < gcHandles.Count; i++) gcHandles[i].Free();
				ListPool<GCHandle>.Release(ref gcHandles);
			}
			UnityEngine.Profiling.Profiler.EndSample();
		}
	}

	public struct JobHandleWithMainThreadWork {
		JobDependencyTracker tracker;
		IEnumerator<JobHandle> coroutine;

		public JobHandleWithMainThreadWork (IEnumerator<JobHandle> handles, JobDependencyTracker tracker) {
			this.coroutine = handles;
			this.tracker = tracker;
		}

		public void Complete () {
			tracker.timeSlice = TimeSlice.Infinite;
			while (coroutine.MoveNext()) {
				coroutine.Current.Complete();
			}
		}

		public System.Collections.IEnumerable CompleteTimeSliced (float maxMillisPerStep) {
			tracker.timeSlice = TimeSlice.MillisFromNow(maxMillisPerStep);
			while (true) {
				if (!coroutine.MoveNext()) yield break;
				while (!coroutine.Current.IsCompleted) {
					yield return null;
					tracker.timeSlice = TimeSlice.MillisFromNow(maxMillisPerStep);
				}
				coroutine.Current.Complete();
				if (tracker.timeSlice.expired) {
					yield return null;
					tracker.timeSlice = TimeSlice.MillisFromNow(maxMillisPerStep);
				}
			}
		}
	}

	enum LinearDependencies : byte {
		Check,
		Enabled,
		Disabled,
	}

	/// <summary>
	/// Automatic dependency tracking for the Unity Job System.
	///
	/// Uses reflection to find the [ReadOnly] and [WriteOnly] attributes on job data struct fields.
	/// These are used to automatically figure out dependencies between jobs.
	///
	/// A job that reads from an array depends on the last job that wrote to that array.
	/// A job that writes to an array depends on the last job that wrote to the array as well as all jobs that read from the array.
	///
	/// <code>
	/// struct ExampleJob : IJob {
	///     public NativeArray<int> someData;
	///
	///     public void Execute () {
	///         // Do something
	///     }
	/// }
	///
	/// void Start () {
	///     var tracker = new JobDependencyTracker();
	///     var data = new NativeArray<int>(100, Allocator.TempJob);
	///     var job1 = new ExampleJob {
	///         someData = data
	///     }.Schedule(tracker);
	///
	///     var job2 = new ExampleJob {
	///         someData = data
	///     }.Schedule(tracker);
	///
	///     // job2 automatically depends on job1 because they both require read/write access to the data array
	/// }
	/// </code>
	///
	/// See: <see cref="IJobExtensions"/>
	/// </summary>
	public class JobDependencyTracker : IAstarPooledObject {
		internal List<NativeArraySlot> slots = ListPool<NativeArraySlot>.Claim();
		DisposeArena arena;
		internal NativeArray<JobHandle> dependenciesScratchBuffer;
		LinearDependencies linearDependencies;
		internal TimeSlice timeSlice = TimeSlice.Infinite;

		public bool forceLinearDependencies {
			get {
				if (linearDependencies == LinearDependencies.Check) SetLinearDependencies(false);
				return linearDependencies == LinearDependencies.Enabled;
			}
		}

		internal struct JobInstance {
			public JobHandle handle;
			public int hash;
#if DEBUG_JOBS
			public string name;
#endif
		}

		internal struct NativeArraySlot {
			public long hash;
			public JobInstance lastWrite;
			public List<JobInstance> lastReads;
			public bool initialized;
			public bool hasWrite;
		}

		// Note: burst compiling even an empty job can avoid the overhead of going from unmanaged to managed code.
		/* [BurstCompile]
		struct JobDispose<T> : IJob where T : struct {
		    [DeallocateOnJobCompletion]
		    [DisableUninitializedReadCheck]
		    public NativeArray<T> data;

		    public void Execute () {
		    }
		}*/

		struct JobRaycastCommandDummy : IJob {
			[ReadOnly]
			public NativeArray<UnityEngine.RaycastCommand> commands;
			[WriteOnly]
			public NativeArray<UnityEngine.RaycastHit> results;

			public void Execute () {}
		}

		/// <summary>
		/// JobHandle that represents a dependency for all jobs.
		/// All native arrays that are written (and have been tracked by this tracker) to will have their final results in them
		/// when the returned job handle is complete.
		/// </summary>
		public JobHandle AllWritesDependency {
			get {
				var handles = new NativeArray<JobHandle>(slots.Count, Allocator.Temp);
				for (int i = 0; i < slots.Count; i++) handles[i] = slots[i].lastWrite.handle;
				var dependencies = JobHandle.CombineDependencies(handles);
				handles.Dispose();
				return dependencies;
			}
		}

		bool supportsMultithreading {
			get {
#if UNITY_WEBGL
				return false;
#else
				return Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount > 0;
#endif
			}
		}

		/// <summary>
		/// Disable dependency tracking and just run jobs one after the other.
		/// This may be faster in some cases since dependency tracking has some overhead.
		/// </summary>
		public void SetLinearDependencies (bool linearDependencies) {
			if (!supportsMultithreading) linearDependencies = true;

			if (linearDependencies) {
				AllWritesDependency.Complete();
			}
			this.linearDependencies = linearDependencies ? LinearDependencies.Enabled : LinearDependencies.Disabled;
		}

		public NativeArray<T> NewNativeArray<T>(int length, Allocator allocator, NativeArrayOptions options = NativeArrayOptions.ClearMemory) where T : unmanaged {
			var res = new NativeArray<T>(length, allocator, options);

			unsafe {
				slots.Add(new NativeArraySlot {
					hash = (long)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(res),
					lastWrite = default,
					lastReads = ListPool<JobInstance>.Claim(),
					initialized = options == NativeArrayOptions.ClearMemory,
				});
			}
			if (this.arena == null) this.arena = new DisposeArena();
			arena.Add(res);
			return res;
		}

		/// <summary>
		/// Schedules a raycast batch command.
		/// Like RaycastCommand.ScheduleBatch, but dependencies are tracked automatically.
		/// </summary>
		public JobHandle ScheduleBatch (NativeArray<UnityEngine.RaycastCommand> commands, NativeArray<UnityEngine.RaycastHit> results, int minCommandsPerJob) {
			if (forceLinearDependencies) {
				UnityEngine.RaycastCommand.ScheduleBatch(commands, results, minCommandsPerJob).Complete();
				return default;
			}

			// Create a dummy structure to allow the analyzer to determine how the job reads/writes data
			var dummy = new JobRaycastCommandDummy { commands = commands, results = results };
			var dependencies = JobDependencyAnalyzer<JobRaycastCommandDummy>.GetDependencies(ref dummy, this);
			var job = UnityEngine.RaycastCommand.ScheduleBatch(commands, results, minCommandsPerJob, dependencies);

			JobDependencyAnalyzer<JobRaycastCommandDummy>.Scheduled(ref dummy, this, job);
			return job;
		}

		/// <summary>Frees the GCHandle when the JobDependencyTracker is disposed</summary>
		public void DeferFree (GCHandle handle, JobHandle dependsOn) {
			if (this.arena == null) this.arena = new DisposeArena();
			this.arena.Add(handle);
		}

#if DEBUG_JOBS
		internal void JobReadsFrom (JobHandle job, long nativeArrayHash, int jobHash, string jobName)
#else
		internal void JobReadsFrom (JobHandle job, long nativeArrayHash, int jobHash)
#endif
		{
			for (int j = 0; j < slots.Count; j++) {
				var slot = slots[j];
				if (slot.hash == nativeArrayHash) {
					// If the job only reads from the array then we just add this job to the list of readers
					slot.lastReads.Add(new JobInstance {
						handle = job,
						hash = jobHash,
#if DEBUG_JOBS
						name = jobName,
#endif
					});
					break;
				}
			}
		}

#if DEBUG_JOBS
		internal void JobWritesTo (JobHandle job, long nativeArrayHash, int jobHash, string jobName)
#else
		internal void JobWritesTo (JobHandle job, long nativeArrayHash, int jobHash)
#endif
		{
			for (int j = 0; j < slots.Count; j++) {
				var slot = slots[j];
				if (slot.hash == nativeArrayHash) {
					// If the job writes to the array then this job is now the last writer
					slot.lastWrite = new JobInstance {
						handle = job,
						hash = jobHash,
#if DEBUG_JOBS
						name = jobName,
#endif
					};
					slot.lastReads.Clear();
					// The array no longer contains uninitialized data.
					// Parts of it may still be uninitialized if the job doesn't write to everything, but that's something that this class cannot track.
					slot.initialized = true;
					slot.hasWrite = true;
					slots[j] = slot;
					break;
				}
			}
		}

		/// <summary>
		/// Disposes this tracker.
		/// This will pool all used lists which makes the GC happy.
		///
		/// Note: It is necessary to call this method to avoid memory leaks if you are using the DeferDispose method. But it's a good thing to do otherwise as well.
		/// It is automatically called if you are using the ObjectPool<T>.Release method.
		/// </summary>
		void Dispose () {
			for (int i = 0; i < slots.Count; i++) ListPool<JobInstance>.Release(slots[i].lastReads);

			slots.Clear();
			if (arena != null) arena.DisposeAll();
			linearDependencies = LinearDependencies.Check;
			if (dependenciesScratchBuffer.IsCreated) dependenciesScratchBuffer.Dispose();
		}

		void IAstarPooledObject.OnEnterPool () {
			Dispose();
		}
	}

	public struct TimeSlice {
		public long endTick;
		public static readonly TimeSlice Infinite = new TimeSlice { endTick = long.MaxValue };
		public bool expired => System.DateTime.UtcNow.Ticks > endTick;
		public static TimeSlice MillisFromNow (float millis) {
			return new TimeSlice { endTick = System.DateTime.UtcNow.Ticks + (long)(millis * 10000) };
		}
	}

	public interface IJobTimeSliced : IJob {
		/// <summary>
		/// Returns true if the job completed.
		/// If false is returned this job may be called again until the job completes.
		/// </summary>
		bool Execute(TimeSlice timeSlice);
	}

	/// <summary>Extension methods for IJob and related interfaces</summary>
	public static class IJobExtensions {
		struct ManagedJob : IJob {
			public GCHandle handle;

			public void Execute () {
				((IJob)handle.Target).Execute();
				handle.Free();
			}
		}

		struct ManagedActionJob : IJob {
			public GCHandle handle;

			public void Execute () {
				((System.Action)handle.Target)();
				handle.Free();
			}
		}

		/// <summary>
		/// Schedule a job with automatic dependency tracking.
		/// You need to have "using Pathfinding.Util" in your script to be able to use this extension method.
		///
		/// See: <see cref="Pathfinding.Util.JobDependencyTracker"/>
		/// </summary>
		public static JobHandle Schedule<T>(this T data, JobDependencyTracker tracker) where T : struct, IJob {
			if (tracker.forceLinearDependencies) {
				data.Run();
				return default;
			} else {
				var job = data.Schedule(JobDependencyAnalyzer<T>.GetDependencies(ref data, tracker));
				JobDependencyAnalyzer<T>.Scheduled(ref data, tracker, job);
				return job;
			}
		}

		/// <summary>Schedules an <see cref="IJobParallelForBatched"/> job with automatic dependency tracking</summary>
		public static JobHandle ScheduleBatch<T>(this T data, int arrayLength, int minIndicesPerJobCount, JobDependencyTracker tracker, JobHandle additionalDependency = default) where T : struct, IJobParallelForBatched {
			if (tracker.forceLinearDependencies) {
				additionalDependency.Complete();
				//data.ScheduleBatch(arrayLength, minIndicesPerJobCount, additionalDependency).Complete();
				data.RunBatch(arrayLength);
				return default;
			} else {
				var job = data.ScheduleBatch(arrayLength, minIndicesPerJobCount, JobDependencyAnalyzer<T>.GetDependencies(ref data, tracker, additionalDependency));

				JobDependencyAnalyzer<T>.Scheduled(ref data, tracker, job);
				return job;
			}
		}

		/// <summary>Schedules a managed job to run in the job system</summary>
		public static JobHandle ScheduleManaged<T>(this T data, JobHandle dependsOn) where T : struct, IJob {
			return new ManagedJob { handle = GCHandle.Alloc(data) }.Schedule(dependsOn);
		}

		/// <summary>Schedules a managed job to run in the job system</summary>
		public static JobHandle ScheduleManaged (this System.Action data, JobHandle dependsOn) {
			return new ManagedActionJob {
					   handle = GCHandle.Alloc(data)
			}.Schedule(dependsOn);
		}

		public static JobHandle GetDependencies<T>(this T data, JobDependencyTracker tracker) where T : struct, IJob {
			if (tracker.forceLinearDependencies) return default;
			else return JobDependencyAnalyzer<T>.GetDependencies(ref data, tracker);
		}

		/// <summary>
		/// Executes this job in the main thread using a coroutine.
		/// Usage:
		/// - 1. Optionally schedule some other jobs before this one (using the dependency tracker)
		/// - 2. Call job.ExecuteMainThreadJob(tracker)
		/// - 3. Iterate over the enumerator until it is finished. Call handle.Complete on all yielded job handles. Usually this only yields once, but if you use the <see cref="JobHandleWithMainThreadWork"/> wrapper it will
		///    yield once for every time slice.
		/// - 4. Continue scheduling other jobs.
		///
		/// You must not schedule other jobs (that may touch the same data) while executing this job.
		///
		/// See: <see cref="Pathfinding.Jobs.JobHandleWithMainThreadWork"/>
		/// </summary>
		public static IEnumerator<JobHandle> ExecuteMainThreadJob<T>(this T data, JobDependencyTracker tracker) where T : struct, IJobTimeSliced {
			if (tracker.forceLinearDependencies) {
				UnityEngine.Profiling.Profiler.BeginSample("Main Thread Work");
				data.Execute();
				UnityEngine.Profiling.Profiler.EndSample();
				yield break;
			}

			var dependsOn = JobDependencyAnalyzer<T>.GetDependencies(ref data, tracker);
			yield return dependsOn;

			while (true) {
				UnityEngine.Profiling.Profiler.BeginSample("Main Thread Work");
				var didComplete = data.Execute(tracker.timeSlice);
				UnityEngine.Profiling.Profiler.EndSample();
				if (didComplete) yield break;
				else yield return new JobHandle();
			}
		}
	}

	internal static class JobDependencyAnalyzerAssociated {
		internal static UnityEngine.Profiling.CustomSampler getDependenciesSampler = UnityEngine.Profiling.CustomSampler.Create("GetDependencies");
		internal static UnityEngine.Profiling.CustomSampler iteratingSlotsSampler = UnityEngine.Profiling.CustomSampler.Create("IteratingSlots");
		internal static UnityEngine.Profiling.CustomSampler initSampler = UnityEngine.Profiling.CustomSampler.Create("Init");
		internal static UnityEngine.Profiling.CustomSampler combineSampler = UnityEngine.Profiling.CustomSampler.Create("Combining");
		internal static int[] tempJobDependencyHashes = new int[16];
		internal static int jobCounter = 1;
	}

	struct JobDependencyAnalyzer<T> where T : struct {
		static ReflectionData reflectionData;

		/// <summary>Offset to the m_Buffer field inside each NativeArray<T></summary>
		// Note: Due to a Unity bug we have to calculate this for NativeArray<int> instead of NativeArray<>. NativeArray<> will return an incorrect value (-16) when using IL2CPP.
		static readonly int BufferOffset = UnsafeUtility.GetFieldOffset(typeof(NativeArray<int>).GetField("m_Buffer", BindingFlags.Instance | BindingFlags.NonPublic));
		struct ReflectionData {
			public int[] fieldOffsets;
			public bool[] writes;
			public bool[] checkUninitializedRead;
			public string[] fieldNames;

			public void Build () {
				// Find the byte offsets within the struct to all m_Buffer fields in all the native arrays in the struct
				var fields = new List<int>();
				var writes = new List<bool>();
				var reads = new List<bool>();
				var names = new List<string>();

				Build(typeof(T), fields, writes, reads, names, BufferOffset, false, false, false);
				this.fieldOffsets = fields.ToArray();
				this.writes = writes.ToArray();
				this.fieldNames = names.ToArray();
				this.checkUninitializedRead = reads.ToArray();
			}

			void Build (System.Type type, List<int> fields, List<bool> writes, List<bool> reads, List<string> names, int offset, bool forceReadOnly, bool forceWriteOnly, bool forceDisableUninitializedCheck) {
				foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) {
					// Check if this field is a NativeArray
					if (field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(NativeArray<>)) {
						fields.Add(offset + UnsafeUtility.GetFieldOffset(field));
						writes.Add(!forceReadOnly && field.GetCustomAttribute(typeof(ReadOnlyAttribute)) == null);
						reads.Add(!forceWriteOnly && !forceDisableUninitializedCheck && field.GetCustomAttribute(typeof(WriteOnlyAttribute)) == null && field.GetCustomAttribute(typeof(DisableUninitializedReadCheckAttribute)) == null);
						names.Add(field.Name);
					} else if (!field.FieldType.IsPrimitive && field.FieldType.IsValueType && !field.FieldType.IsEnum) {
						// Recurse to handle nested types
						bool readOnly = field.GetCustomAttribute(typeof(ReadOnlyAttribute)) != null;
						bool writeOnly = field.GetCustomAttribute(typeof(WriteOnlyAttribute)) != null;
						bool disableUninitializedCheck = field.GetCustomAttribute(typeof(DisableUninitializedReadCheckAttribute)) != null;
						Build(field.FieldType, fields, writes, reads, names, offset + UnsafeUtility.GetFieldOffset(field), readOnly, writeOnly, disableUninitializedCheck);
					}
				}
			}
		}

		static void initReflectionData () {
			if (reflectionData.fieldOffsets == null) {
				reflectionData.Build();
			}
		}

		static bool HasHash (int[] hashes, int hash, int count) {
			for (int i = 0; i < count; i++) if (hashes[i] == hash) return true;
			return false;
		}

#if ENABLE_UNITY_COLLECTIONS_CHECKS
		public static void SetSafetyHandle (ref T data, AtomicSafetyHandle safetyHandle) {
			initReflectionData();
			var offsets = reflectionData.fieldOffsets;
			unsafe {
				// Note: data is a struct. It is stored on the stack and can thus not be moved by the GC.
				// Therefore we do not need to pin it first.
				// It is guaranteed to be stored on the stack since the Schedule method takes the data parameter by value and not by reference.
				byte* dataPtr = (byte*)UnsafeUtility.AddressOf(ref data);

				for (int i = 0; i < offsets.Length; i++) {
					// This is the NativeArray<T> field (offsets[i] includes the offset to the field NativeArray<>.m_buffer, so we compensate by subtracting it away again)
					void* ptr = dataPtr + offsets[i] - BufferOffset;
#if MODULE_COLLECTIONS_0_11_OR_NEWER
					// Collections 0.11-preview removes the CompilerServices.Unsafe dll.
					Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref Unity.Collections.LowLevel.Unsafe.UnsafeUtility.AsRef<NativeArray<byte> >(ptr), safetyHandle);
#else
					Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref System.Runtime.CompilerServices.Unsafe.AsRef<NativeArray<byte> >(ptr), safetyHandle);
#endif
				}
			}
		}
#endif

		/// <summary>Returns the dependencies for the given job.</summary>
		/// <param name="data">Job data. Must be allocated on the stack.</param>
		public static JobHandle GetDependencies (ref T data, JobDependencyTracker tracker) {
			return GetDependencies(ref data, tracker, default, false);
		}

		public static JobHandle GetDependencies (ref T data, JobDependencyTracker tracker, JobHandle additionalDependency) {
			return GetDependencies(ref data, tracker, additionalDependency, true);
		}

		static JobHandle GetDependencies (ref T data, JobDependencyTracker tracker, JobHandle additionalDependency, bool useAdditionalDependency) {
			//JobDependencyAnalyzerAssociated.getDependenciesSampler.Begin();
			//JobDependencyAnalyzerAssociated.initSampler.Begin();
			if (!tracker.dependenciesScratchBuffer.IsCreated) tracker.dependenciesScratchBuffer = new NativeArray<JobHandle>(16, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
			var dependencies = tracker.dependenciesScratchBuffer;
			var slots = tracker.slots;
			var dependencyHashes = JobDependencyAnalyzerAssociated.tempJobDependencyHashes;

			int numDependencies = 0;

			//JobDependencyAnalyzerAssociated.initSampler.End();
			initReflectionData();
#if DEBUG_JOBS
			string dependenciesDebug = "";
#endif
			unsafe {
				// Note: data is a struct. It is stored on the stack and can thus not be moved by the GC.
				// Therefore we do not need to pin it first.
				// It is guaranteed to be stored on the stack since the Schedule method takes the data parameter by value and not by reference.
				byte* dataPtr = (byte*)UnsafeUtility.AddressOf(ref data);

				var offsets = reflectionData.fieldOffsets;
				for (int i = 0; i < offsets.Length; i++) {
					// This is the internal value of the m_Buffer field of the NativeArray
					void* nativeArrayBufferPtr = *(void**)(dataPtr + offsets[i]);

					// Use the pointer as a hash to uniquely identify a NativeArray
					var hash = (long)nativeArrayBufferPtr;

					//JobDependencyAnalyzerAssociated.iteratingSlotsSampler.Begin();
					for (int j = 0; j <= slots.Count; j++) {
						// No slot found. Add a new one
						if (j == slots.Count) {
							slots.Add(new JobDependencyTracker.NativeArraySlot {
								hash = hash,
								lastWrite = default,
								lastReads = ListPool<JobDependencyTracker.JobInstance>.Claim(),
								initialized = true, // We don't know anything about the array, so assume it contains initialized data. JobDependencyTracker.NewNativeArray should be used otherwise.
								hasWrite = false,
							});
						}

						// Check if we know about this NativeArray yet
						var slot = slots[j];
						if (slot.hash == hash) {
							if (reflectionData.checkUninitializedRead[i] && !slot.initialized) {
								throw new System.InvalidOperationException("A job tries to read from the native array " + typeof(T).Name + "." + reflectionData.fieldNames[i] + " which contains uninitialized data");
							}

							if (slot.hasWrite && !HasHash(dependencyHashes, slot.lastWrite.hash, numDependencies)) {
								// Reads/writes always depend on the last write to the native array
								dependencies[numDependencies] = slot.lastWrite.handle;
								dependencyHashes[numDependencies] = slot.lastWrite.hash;
								numDependencies++;
								if (numDependencies >= dependencies.Length) throw new System.Exception("Too many dependencies for job");
#if DEBUG_JOBS
								dependenciesDebug += slot.lastWrite.name + " ";
#endif
							}

							// If we want to write to the array we additionally depend on all previous reads of the array
							if (reflectionData.writes[i]) {
								for (int q = 0; q < slot.lastReads.Count; q++) {
									if (!HasHash(dependencyHashes, slot.lastReads[q].hash, numDependencies)) {
										dependencies[numDependencies] = slot.lastReads[q].handle;
										dependencyHashes[numDependencies] = slot.lastReads[q].hash;
										numDependencies++;
										if (numDependencies >= dependencies.Length) throw new System.Exception("Too many dependencies for job");
#if DEBUG_JOBS
										dependenciesDebug += slot.lastReads[q].name + " ";
#endif
									}
								}
							}
							break;
						}
					}
					//JobDependencyAnalyzerAssociated.iteratingSlotsSampler.End();
				}

				if (useAdditionalDependency) {
					dependencies[numDependencies] = additionalDependency;
					numDependencies++;
#if DEBUG_JOBS
					dependenciesDebug += "[additional dependency]";
#endif
				}

#if DEBUG_JOBS
				UnityEngine.Debug.Log(typeof(T) + " depends on " + dependenciesDebug);
#endif

				if (numDependencies == 0) {
					return default;
				} else if (numDependencies == 1) {
					return dependencies[0];
				} else {
					//JobDependencyAnalyzerAssociated.combineSampler.Begin();
					return JobHandle.CombineDependencies(dependencies.Slice(0, numDependencies));
					//JobDependencyAnalyzerAssociated.combineSampler.End();
				}
			}
		}

		internal static void Scheduled (ref T data, JobDependencyTracker tracker, JobHandle job) {
			unsafe {
				int jobHash = JobDependencyAnalyzerAssociated.jobCounter++;
				// Note: data is a struct. It is stored on the stack and can thus not be moved by the GC.
				// Therefore we do not need to pin it first.
				// It is guaranteed to be stored on the stack since the Schedule method takes the data parameter by value and not by reference.
				byte* dataPtr = (byte*)UnsafeUtility.AddressOf(ref data);
				for (int i = 0; i < reflectionData.fieldOffsets.Length; i++) {
					// This is the internal value of the m_Buffer field of the NativeArray
					void* nativeArrayBufferPtr = *(void**)(dataPtr + reflectionData.fieldOffsets[i]);

					// Use the pointer as a hash to uniquely identify a NativeArray
					var hash = (long)nativeArrayBufferPtr;
#if DEBUG_JOBS
					if (reflectionData.writes[i]) tracker.JobWritesTo(job, hash, jobHash, typeof(T).Name);
					else tracker.JobReadsFrom(job, hash, jobHash, typeof(T).Name);
#else
					if (reflectionData.writes[i]) tracker.JobWritesTo(job, hash, jobHash);
					else tracker.JobReadsFrom(job, hash, jobHash);
#endif
				}
			}
		}
	}
}
