using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Pathfinding.Jobs;
using Unity.Mathematics;

namespace Pathfinding.Jobs {
	public struct RaycastAllCommand {
		int maxHits;
		public readonly float minStep;

		NativeArray<RaycastHit> results;
		NativeArray<RaycastHit> semiResults;
		NativeArray<RaycastCommand> commands;

		[BurstCompile]
		private struct JobCreateCommands : IJobParallelFor {
			public NativeArray<RaycastCommand> commands;
			[ReadOnly]
			public NativeArray<RaycastHit> raycastHits;

			public float minStep;

			public void Execute (int index) {
				var rayHit = raycastHits[index];

				if (rayHit.normal != default(Vector3)) {
					var previousCommand = commands[index];
					// Little hack to bypass same collider hit in specific cases
					var point = rayHit.point + previousCommand.direction.normalized * minStep;
					var distance = previousCommand.distance - (point - previousCommand.from).magnitude;
#if UNITY_2022_2_OR_NEWER
					// TODO: In 2022.2 with the 'hit multiple faces' option, this whole class might be redundant.
					var queryParameters = new QueryParameters(previousCommand.queryParameters.layerMask, false, QueryTriggerInteraction.Ignore, false);
					commands[index] = new RaycastCommand(point, previousCommand.direction, queryParameters, distance);
#else
					commands[index] = new RaycastCommand(point, previousCommand.direction, distance, previousCommand.layerMask, 1);
#endif
				} else {
					commands[index] = default(RaycastCommand);
				}
			}
		}

		[BurstCompile]
		private struct JobCombineResults : IJob {
			public int maxHits;
			[ReadOnly]
			public NativeArray<RaycastHit> semiResults;
			public NativeArray<RaycastHit> results;
			public void Execute () {
				int layerStride = semiResults.Length / maxHits;

				for (int i = 0; i < layerStride; i++) {
					int layerOffset = 0;

					for (int j = maxHits - 1; j >= 0; j--) {
						if (math.any(semiResults[i + j*layerStride].normal)) {
							results[i + layerOffset] = semiResults[i + j*layerStride];
							layerOffset += layerStride;
						}
					}
				}
			}
		}

		/// <summary>
		/// Jobified version of <see cref="Physics.RaycastNonAlloc"/>
		/// </summary>
		/// <param name="commands">Array of commands to perform. Each <see cref="RaycastCommand.maxHits"/> should be 1</param>
		/// <param name="maxHits">Max hits count per command</param>
		/// <param name="minStep">Minimal distance each Raycast should progress</param>
		public RaycastAllCommand(NativeArray<RaycastCommand> commands, NativeArray<RaycastHit> results, int maxHits, Allocator allocator, JobDependencyTracker dependencyTracker, float minStep = 0.0001f) {
			if (maxHits <= 0) throw new System.ArgumentException("maxHits should be greater than zero");
			if (results.Length < commands.Length * maxHits) throw new System.ArgumentException("Results array length does not match maxHits count");
			if (minStep < 0f) throw new System.ArgumentException("minStep should be more or equal to zero");

			this.results = results;
			this.maxHits = maxHits;
			this.minStep = minStep;
			this.commands = commands;

			semiResults = dependencyTracker.NewNativeArray<RaycastHit>(maxHits * commands.Length, allocator);
		}

		public JobHandle Schedule (JobHandle dependency) {
			for (int i = 0; i < maxHits; i++) {
				var semiResultsPart = semiResults.GetSubArray(i*commands.Length, commands.Length);
				dependency = RaycastCommand.ScheduleBatch(commands, semiResultsPart, 256, dependency);
				if (i < maxHits - 1) {
					var filter = new JobCreateCommands {
						commands = commands,
						raycastHits = semiResultsPart,
						minStep = minStep
					};
					dependency = filter.Schedule(commands.Length, 256, dependency);
				}
			}

			return new JobCombineResults {
					   semiResults = semiResults,
					   maxHits = maxHits,
					   results = results
			}.Schedule(dependency);
		}
	}
}
