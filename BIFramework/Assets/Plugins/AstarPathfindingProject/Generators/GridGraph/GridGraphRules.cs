using System.Collections.Generic;

namespace Pathfinding {
	using Pathfinding.Serialization;
	using Pathfinding.Jobs;
	using Unity.Jobs;
	using Unity.Collections;
	using Unity.Burst;
	using Unity.Mathematics;

	public class CustomGridGraphRuleEditorAttribute : System.Attribute {
		public System.Type type;
		public string name;
		public CustomGridGraphRuleEditorAttribute(System.Type type, string name) {
			this.type = type;
			this.name = name;
		}
	}

	/// <summary>
	/// Container for all rules in a grid graph.
	///
	/// <code>
	/// // Get the first grid graph in the scene
	/// var gridGraph = AstarPath.active.data.gridGraph;
	///
	/// gridGraph.rules.AddRule(new RuleAnglePenalty {
	///     penaltyScale = 10000,
	///     curve = AnimationCurve.Linear(0, 0, 90, 1),
	/// });
	/// </code>
	///
	/// See: <see cref="Pathfinding.GridGraph.rules"/>
	/// See: grid-rules (view in online documentation for working links)
	/// </summary>
	[JsonOptIn]
	public class GridGraphRules {
		List<System.Action<Context> >[] jobSystemCallbacks;
		List<System.Action<Context> >[] mainThreadCallbacks;

		/// <summary>List of all rules</summary>
		[JsonMember]
		List<GridGraphRule> rules = new List<GridGraphRule>();

		long lastHash;

		/// <summary>Context for when scanning or updating a graph</summary>
		public class Context {
			/// <summary>Graph which is being scanned or updated</summary>
			public GridGraph graph;
			/// <summary>Data for all the nodes as NativeArrays</summary>
			public GridGraph.GridGraphScanData data;
			/// <summary>Tracks job dependencies. Use when scheduling jobs.</summary>
			public JobDependencyTracker tracker;
		}

		public void AddRule (GridGraphRule rule) {
			rules.Add(rule);
			lastHash = -1;
		}

		public void RemoveRule (GridGraphRule rule) {
			rules.Remove(rule);
			lastHash = -1;
		}

		public IReadOnlyList<GridGraphRule> GetRules () {
			if (rules == null) rules = new List<GridGraphRule>();
			return rules.AsReadOnly();
		}

		long Hash () {
			long hash = 196613;

			for (int i = 0; i < rules.Count; i++) {
				if (rules[i] != null && rules[i].enabled) hash = hash * 1572869 ^ (long)rules[i].Hash;
			}
			return hash;
		}

		public void RebuildIfNecessary () {
			var newHash = Hash();

			if (newHash == lastHash && jobSystemCallbacks != null && mainThreadCallbacks != null) return;
			lastHash = newHash;
			Rebuild();
		}

		public void Rebuild () {
			rules = rules ?? new List<GridGraphRule>();
			jobSystemCallbacks = jobSystemCallbacks ?? new List<System.Action<Context> >[5];
			for (int i = 0; i < jobSystemCallbacks.Length; i++) {
				if (jobSystemCallbacks[i] != null) jobSystemCallbacks[i].Clear();
			}
			mainThreadCallbacks = mainThreadCallbacks ?? new List<System.Action<Context> >[5];
			for (int i = 0; i < mainThreadCallbacks.Length; i++) {
				if (mainThreadCallbacks[i] != null) mainThreadCallbacks[i].Clear();
			}
			for (int i = 0; i < rules.Count; i++) {
				if (rules[i].enabled) rules[i].Register(this);
			}
		}

		public void DisposeUnmanagedData () {
			if (rules != null) {
				for (int i = 0; i < rules.Count; i++) {
					if (rules[i] != null) {
						rules[i].DisposeUnmanagedData();
						rules[i].SetDirty();
					}
				}
			}
		}

		static void CallActions (List<System.Action<Context> > actions, Context context) {
			if (actions != null) {
				try {
					for (int i = 0; i < actions.Count; i++) actions[i](context);
				} catch (System.Exception e) {
					UnityEngine.Debug.LogException(e);
				}
			}
		}

		/// <summary>
		/// Executes the rules for the given pass.
		/// Call handle.Complete on, or wait for, all yielded job handles.
		/// </summary>
		public IEnumerator<JobHandle> ExecuteRule (GridGraphRule.Pass rule, Context context) {
			if (jobSystemCallbacks == null) Rebuild();
			CallActions(jobSystemCallbacks[(int)rule], context);

			if (mainThreadCallbacks[(int)rule] != null && mainThreadCallbacks[(int)rule].Count > 0) {
				if (!context.tracker.forceLinearDependencies) yield return context.tracker.AllWritesDependency;
				CallActions(mainThreadCallbacks[(int)rule], context);
			}
		}

		/// <summary>
		/// Adds a pass callback that uses the job system.
		/// This rule should only schedule jobs using the `Context.tracker` dependency tracker. Data is not safe to access directly in the callback
		/// </summary>
		public void AddJobSystemPass (GridGraphRule.Pass pass, System.Action<Context> action) {
			var index = (int)pass;

			if (jobSystemCallbacks[index] == null) {
				jobSystemCallbacks[index] = new List<System.Action<Context> >();
			}
			jobSystemCallbacks[index].Add(action);
		}

		/// <summary>
		/// Adds a pass callback that runs in the main thread.
		/// The callback may access and modify any data in the context.
		/// You do not need to schedule jobs in order to access the data.
		///
		/// Warning: Not all data in the Context is valid for every pass. For example you cannot access node connections in the BeforeConnections pass
		/// since they haven't been calculated yet.
		///
		/// This is a bit slower than <see cref="AddJobSystemPass"/> since parallelism and the burst compiler cannot be used.
		/// But if you need to use non-thread-safe APIs or data then this is a good choice.
		/// </summary>
		public void AddMainThreadPass (GridGraphRule.Pass pass, System.Action<Context> action) {
			var index = (int)pass;

			if (mainThreadCallbacks[index] == null) {
				mainThreadCallbacks[index] = new List<System.Action<Context> >();
			}
			mainThreadCallbacks[index].Add(action);
		}

		/// <summary>Deprecated: Use AddJobSystemPass or AddMainThreadPass instead</summary>
		[System.Obsolete("Use AddJobSystemPass or AddMainThreadPass instead")]
		public void Add (GridGraphRule.Pass pass, System.Action<Context> action) {
			AddJobSystemPass(pass, action);
		}
	}

	/// <summary>
	/// Custom rule for a grid graph.
	/// See: <see cref="Pathfinding.GridGraphRules"/>
	/// See: grid-rules (view in online documentation for working links)
	/// </summary>
	[JsonDynamicType]
	public abstract class GridGraphRule {
		/// <summary>Only enabled rules are executed</summary>
		[JsonMember]
		public bool enabled = true;
		int dirty = 1;

		/// <summary>
		/// Where in the scanning process a rule will be executed.
		/// Check the documentation for <see cref="GridGraphScanData"/> to see which data fields are valid in which passes.
		/// </summary>
		public enum Pass {
			/// <summary>
			/// Before the collision testing phase but after height testing.
			/// This is very early. Most data is not valid by this point.
			///
			/// You can use this if you want to modify the node positions and still have it picked up by the collision testing code.
			/// </summary>
			BeforeCollision,
			/// <summary>
			/// Before connections are calculated.
			/// At this point height testing, collision testing has been done (if they are enabled).
			///
			/// This is the most common pass to use.
			/// If you are modifying walkability here then connections and erosion will be calculated properly.
			/// </summary>
			BeforeConnections,
			/// <summary>
			/// After connections are calculated.
			///
			/// If you are modifying connections directly you should do that in this pass.
			///
			/// Note: If erosion is used then this pass will be executed twice. One time before erosion and one time after erosion
			/// when the connections are calculated again.
			/// </summary>
			AfterConnections,
			/// <summary>
			/// After erosion is calculated but before connections have been recalculated.
			///
			/// If no erosion is used then this pass will not be executed.
			/// </summary>
			AfterErosion,
			/// <summary>
			/// After everything else.
			/// This pass is executed after everything else is done.
			/// You should not modify walkability in this pass because then the node connections will not be up to date.
			/// </summary>
			PostProcess,
		}

		/// <summary>
		/// Hash of the settings for this rule.
		/// The <see cref="Register"/> method will be called again whenever the hash changes.
		/// If the hash does not change it is assumed that the <see cref="Register"/> method does not need to be called again.
		/// </summary>
		public virtual int Hash => dirty;

		/// <summary>
		/// Call if you have changed any setting of the rule.
		/// This will ensure that any cached data the rule uses is rebuilt.
		/// If you do not do this then any settings changes may not affect the graph when it is rescanned or updated.
		/// </summary>
		public virtual void SetDirty () {
			dirty++;
		}

		/// <summary>
		/// Called when the rule is removed or the graph is destroyed.
		/// Use this to e.g. clean up any NativeArrays that the rule uses.
		///
		/// Note: The rule should remain valid after this method has been called.
		/// However the <see cref="Register"/> method is guaranteed to be called before the rule is executed again.
		/// </summary>
		public virtual void DisposeUnmanagedData () {
		}

		/// <summary>Does preprocessing and adds callbacks to the <see cref="GridGraphRules"/> object</summary>
		public virtual void Register (GridGraphRules rules) {
		}

		public interface IConnectionFilter {
			bool IsValidConnection(int dataIndex, int dataX, int dataLayer, int dataZ, int direction);
		}

		public interface INodeModifier {
			void ModifyNode(int dataIndex, int dataX, int dataLayer, int dataZ);
		}

		/// <summary>Iterate through all nodes.</summary>
		/// <param name="bounds">Sub-rectangle of the grid graph that is being updated/scanned</param>
		/// <param name="nodeNormals">Data for all node normals. This is used to determine if a node exists (important for layered grid graphs).</param>
		/// <param name="callback">The ModifyNode method on the callback struct will be called for each node.</param>
		public static void ForEachNode<T>(IntBounds bounds, NativeArray<float4> nodeNormals, ref T callback) where T : struct, INodeModifier {
			var size = bounds.size;

			int i = 0;

			for (int y = 0; y < size.y; y++) {
				for (int z = 0; z < size.z; z++) {
					for (int x = 0; x < size.x; x++, i++) {
						// Check if the node exists at all
						// This is important for layered grid graphs
						// A normal is never zero otherwise
						if (math.any(nodeNormals[i])) {
							callback.ModifyNode(i, x, y, z);
						}
					}
				}
			}
		}

		public static void FilterNodeConnections<T>(IntBounds bounds, NativeArray<int> nodeConnections, bool layeredDataLayout, ref T filter) where T : struct, IConnectionFilter {
			var size = bounds.size;
			int nodeIndex = 0;


			for (int y = 0; y < size.y; y++) {
				for (int z = 0; z < size.z; z++) {
					for (int x = 0; x < size.x; x++, nodeIndex++) {
						var conn = nodeConnections[nodeIndex];
						if (layeredDataLayout) {
							// Layered grid graph
							for (int i = 0; i < 8; i++) {
								if (((conn >> LevelGridNode.ConnectionStride*i) & LevelGridNode.ConnectionMask) != LevelGridNode.NoConnection && !filter.IsValidConnection(nodeIndex, x, y, z, i)) {
									conn |= LevelGridNode.NoConnection << LevelGridNode.ConnectionStride*i;
								}
							}
						} else {
							// Normal grid graph
							// Iterate through all connections on the node
							for (int i = 0; i < 8; i++) {
								if ((conn & (1 << i)) != 0 && !filter.IsValidConnection(nodeIndex, x, y, z, i)) {
									conn &= ~(1 << i);
								}
							}
						}
						nodeConnections[nodeIndex] = conn;
					}
				}
			}
		}

		/// <summary>
		/// Returns the data index for a node's neighbour in the given direction.
		///
		/// The bounds, nodeConnections and layeredDataLayout fields can be retrieved from the <see cref="GridGraphRules.Context"/>.data object.
		///
		/// Returns: Null if the node has no connection in that direction. Otherwise the data index for that node is returned.
		///
		/// See: gridgraphrule-connection-filter (view in online documentation for working links) for example usage.
		/// </summary>
		/// <param name="bounds">Sub-rectangle of the grid graph that is being updated/scanned</param>
		/// <param name="nodeConnections">Data for all node connections</param>
		/// <param name="layeredDataLayout">True if this is a layered grid graph</param>
		/// <param name="dataX">X coordinate in the data arrays for the node for which you want to get a neighbour</param>
		/// <param name="dataLayer">Layer (Y) coordinate in the data arrays for the node for which you want to get a neighbour</param>
		/// <param name="dataZ">Z coordinate in the data arrays for the node for which you want to get a neighbour</param>
		/// <param name="direction">Direction to the neighbour. See \reflink{GridNode.HasConnectionInDirection}.</param>
		public static int? GetNeighbourDataIndex (IntBounds bounds, NativeArray<int> nodeConnections, bool layeredDataLayout, int dataX, int dataLayer, int dataZ, int direction) {
			// Find the coordinates of the adjacent node
			var dx = GridGraph.neighbourXOffsets[direction];
			var dz = GridGraph.neighbourZOffsets[direction];

			int nx = dataX + dx;
			int nz = dataZ + dz;

			// For valid nodeConnections arrays this is not necessary as out of bounds connections are not valid and it will thus be caught below in the 'has connection' check.
			// But let's be safe in case users do something weird
			if (nx < 0 || nz < 0 || nx >= bounds.size.x || nz >= bounds.size.z) return null;

			// The data arrays are laid out row by row
			const int xstride = 1;
			var zstride = bounds.size.x;
			var ystride = bounds.size.x * bounds.size.z;

			var dataIndex = dataLayer * ystride + dataZ * zstride + dataX * xstride;
			var neighbourDataIndex = nz * zstride + nx * xstride;

			if (layeredDataLayout) {
				// In a layered grid graph we need to account for nodes in different layers
				var ny = nodeConnections[dataIndex] >> LevelGridNode.ConnectionStride*direction & LevelGridNode.ConnectionMask;
				if (ny == LevelGridNode.NoConnection) return null;
				neighbourDataIndex += ny * ystride;
			} else
			if ((nodeConnections[dataIndex] & (1 << direction)) == 0) return null;

			return neighbourDataIndex;
		}
	}
}
