using System.Collections.Generic;
using Pathfinding.Util;
using UnityEngine;
using Unity.Jobs;
using UnityEngine.Profiling;

namespace Pathfinding {
	using Pathfinding.Drawing;

	/// <summary>
	/// Holds a hierarchical graph to speed up certain pathfinding queries.
	///
	/// A common type of query that needs to be very fast is on the form 'is this node reachable from this other node'.
	/// This is for example used when picking the end node of a path. The end node is determined as the closest node to the end point
	/// that can be reached from the start node.
	///
	/// This data structure's primary purpose is to keep track of which connected component each node is contained in, in order to make such queries fast.
	///
	/// See: https://en.wikipedia.org/wiki/Connected_component_(graph_theory)
	///
	/// A connected component is a set of nodes such that there is a valid path between every pair of nodes in that set.
	/// Thus the query above can simply be answered by checking if they are in the same connected component.
	/// The connected component is exposed on nodes as the <see cref="Pathfinding.GraphNode.Area"/> property and on this class using the <see cref="GetArea"/> method.
	///
	/// In the image below (showing a 200x200 grid graph) each connected component is colored using a separate color.
	/// The actual color doesn't signify anything in particular however, only that they are different.
	/// [Open online documentation to see images]
	///
	/// Prior to version 4.2 the connected components were just a number stored on each node, and when a graph was updated
	/// the connected components were completely recalculated. This can be done relatively efficiently using a flood filling
	/// algorithm (see https://en.wikipedia.org/wiki/Flood_fill) however it still requires a pass through every single node
	/// which can be quite costly on larger graphs.
	///
	/// This class instead builds a much smaller graph that still respects the same connectivity as the original graph.
	/// Each node in this hierarchical graph represents a larger number of real nodes that are one single connected component.
	/// Take a look at the image below for an example. In the image each color is a separate hierarchical node, and the black connections go between the center of each hierarchical node.
	///
	/// [Open online documentation to see images]
	///
	/// With the hierarchical graph, the connected components can be calculated by flood filling the hierarchical graph instead of the real graph.
	/// Then when we need to know which connected component a node belongs to, we look up the connected component of the hierarchical node the node belongs to.
	///
	/// The benefit is not immediately obvious. The above is just a bit more complicated way to accomplish the same thing. However the real benefit comes when updating the graph.
	/// When the graph is updated, all hierarchical nodes which contain any node that was affected by the update is removed completely and then once all have been removed new hierarchical nodes are recalculated in their place.
	/// Once this is done the connected components of the whole graph can be updated by flood filling only the hierarchical graph. Since the hierarchical graph is vastly smaller than the real graph, this is significantly faster.
	///
	/// [Open online documentation to see videos]
	///
	/// So finally using all of this, the connected components of the graph can be recalculated very quickly as the graph is updated.
	/// The effect of this grows larger the larger the graph is, and the smaller the graph update is. Making a small update to a 1000x1000 grid graph is on the order of 40 times faster with these optimizations.
	/// When scanning a graph or making updates to the whole graph at the same time there is however no speed boost. In fact due to the extra complexity it is a bit slower, however after profiling the extra time seems to be mostly insignificant compared to the rest of the cost of scanning the graph.
	///
	/// [Open online documentation to see videos]
	///
	/// See: <see cref="Pathfinding.PathUtilities.IsPathPossible"/>
	/// See: <see cref="Pathfinding.NNConstraint"/>
	/// See: <see cref="Pathfinding.GraphNode.Area"/>
	/// </summary>
	public class HierarchicalGraph {
		const int Tiling = 16;
		const int MaxChildrenPerNode = Tiling * Tiling;
		const int MinChildrenPerNode = MaxChildrenPerNode/2;

		List<GraphNode>[] children = new List<GraphNode>[0];
		List<int>[] connections = new List<int>[0];
		int[] areas = new int[0];
		byte[] dirty = new byte[0];

		public int version { get; private set; }
		public System.Action onConnectedComponentsChanged;

		System.Action<GraphNode> connectionCallback;

		Queue<GraphNode> temporaryQueue = new Queue<GraphNode>();
		List<GraphNode> currentChildren = null;
		List<int> currentConnections = null;
		int currentHierarchicalNodeIndex;
		Stack<int> temporaryStack = new Stack<int>();

		int numDirtyNodes = 0;
		GraphNode[] dirtyNodes = new GraphNode[128];

		Stack<int> freeNodeIndices = new Stack<int>();

		int gizmoVersion = 0;

		JobHandle lastJob;

		public HierarchicalGraph () {
			// Cache this callback to avoid allocating a new one every time the FindHierarchicalNodeChildren method is called.
			// It is a bit ugly to have to use member variables for the state information in that method, but I see no better way.
			connectionCallback = (GraphNode neighbour) => {
				var hIndex = neighbour.HierarchicalNodeIndex;
				if (hIndex == 0) {
					if (currentChildren.Count < MaxChildrenPerNode && neighbour.Walkable /* && (((GridNode)currentChildren[0]).XCoordinateInGrid/Tiling == ((GridNode)neighbour).XCoordinateInGrid/Tiling) && (((GridNode)currentChildren[0]).ZCoordinateInGrid/Tiling == ((GridNode)neighbour).ZCoordinateInGrid/Tiling)*/) {
						neighbour.HierarchicalNodeIndex = currentHierarchicalNodeIndex;
						temporaryQueue.Enqueue(neighbour);
						currentChildren.Add(neighbour);
					}
				} else if (hIndex != currentHierarchicalNodeIndex && !currentConnections.Contains(hIndex)) {
					// The Contains call can in theory be very slow as an hierarchical node may be adjacent to an arbitrary number of nodes.
					// However in practice due to how the hierarchical nodes are constructed they will only be adjacent to a smallish (≈4-6) number of other nodes.
					// So a Contains call will be much faster than say a Set lookup.
					currentConnections.Add(hIndex);
				}
			};

			Grow();
		}

		void Grow () {
			var newChildren = new List<GraphNode>[System.Math.Max(64, children.Length*2)];
			var newConnections = new List<int>[newChildren.Length];
			var newAreas = new int[newChildren.Length];
			var newDirty = new byte[newChildren.Length];

			children.CopyTo(newChildren, 0);
			connections.CopyTo(newConnections, 0);
			areas.CopyTo(newAreas, 0);
			dirty.CopyTo(newDirty, 0);

			// Create all necessary lists for the new nodes
			// Iterate in reverse so that when popping from the freeNodeIndices
			// stack we get numbers from smallest to largest (this is not
			// necessary, but it makes the starting colors be a bit nicer when
			// visualized in the scene view).
			for (int i = newChildren.Length - 1; i >= children.Length; i--) {
				newChildren[i] = ListPool<GraphNode>.Claim(MaxChildrenPerNode);
				newConnections[i] = new List<int>();
				if (i > 0) freeNodeIndices.Push(i);
			}

			children = newChildren;
			connections = newConnections;
			areas = newAreas;
			dirty = newDirty;
		}

		int GetHierarchicalNodeIndex () {
			if (freeNodeIndices.Count == 0) Grow();
			return freeNodeIndices.Pop();
		}

		internal void OnCreatedNode (GraphNode node) {
			AddDirtyNode(node);
		}

		internal void OnDestroyedNode (GraphNode node) {
			AddDirtyNode(node);
		}

		void CompactifyDirtyNodes () {
			int k = numDirtyNodes;

			numDirtyNodes = 0;
			for (int i = 0; i < k; i++) {
				if (!dirtyNodes[i].Destroyed) {
					dirtyNodes[numDirtyNodes] = dirtyNodes[i];
					numDirtyNodes++;
				} else {
					// Mark the associated hierarchical node as dirty to ensure it is recalculated or removed later.
					// If we don't do this then hierarchical nodes in which all nodes are destroyed will leak.
					dirty[dirtyNodes[i].HierarchicalNodeIndex] = 1;
				}
			}
			// Clear end of array
			for (int i = numDirtyNodes; i < k; i++) {
				dirtyNodes[i] = null;
			}

			if (numDirtyNodes >= dirtyNodes.Length) throw new System.Exception("Failed to compactify dirty nodes array. This should not happen. " + numDirtyNodes + " " + dirtyNodes.Length);
		}

		/// <summary>
		/// Marks this node as dirty because it's connectivity or walkability has changed.
		/// This must be called by node classes after any connectivity/walkability changes have been made to them.
		///
		/// See: <see cref="GraphNode.SetConnectivityDirty"/>
		/// </summary>
		public void AddDirtyNode (GraphNode node) {
			if (!node.IsHierarchicalNodeDirty) {
				node.IsHierarchicalNodeDirty = true;
				// While the dirtyNodes array is guaranteed to be large enough to hold all nodes in the graphs
				// the array may also end up containing many destroyed nodes. This can in rare cases cause it to go out of bounds.
				// In that case we need to go through the array and filter out any destroyed nodes while making sure to mark their
				// corresponding hierarchical nodes as being dirty.
				if (numDirtyNodes < dirtyNodes.Length) {
					dirtyNodes[numDirtyNodes] = node;
					numDirtyNodes++;
				} else {
					CompactifyDirtyNodes();
					AddDirtyNode(node);
				}
			}
		}

		public void ReserveNodeIndices (int nodeIndexCount) {
			Memory.Realloc(ref dirtyNodes, nodeIndexCount);
		}

		public int NumConnectedComponents { get; private set; }

		/// <summary>Get the connected component index of a hierarchical node</summary>
		public uint GetConnectedComponent (int hierarchicalNodeIndex) {
			return (uint)areas[hierarchicalNodeIndex];
		}

		void RemoveHierarchicalNode (int hierarchicalNode, bool removeAdjacentSmallNodes) {
			freeNodeIndices.Push(hierarchicalNode);
			var conns = connections[hierarchicalNode];

			for (int i = 0; i < conns.Count; i++) {
				var adjacentHierarchicalNode = conns[i];
				// If dirty, this node will be removed later anyway, so don't bother doing anything with it.
				if (dirty[adjacentHierarchicalNode] != 0) continue;

				if (removeAdjacentSmallNodes && children[adjacentHierarchicalNode].Count < MinChildrenPerNode) {
					dirty[adjacentHierarchicalNode] = 2;
					RemoveHierarchicalNode(adjacentHierarchicalNode, false);
				} else {
					// Remove the connection from the other node to this node as we are removing this node.
					connections[adjacentHierarchicalNode].Remove(hierarchicalNode);
				}
			}
			conns.Clear();

			var nodeChildren = children[hierarchicalNode];

			// Ensure all children of dirty hierarchical nodes are included in the recalculation
			for (int i = 0; i < nodeChildren.Count; i++) {
				if (!nodeChildren[i].Destroyed) AddDirtyNode(nodeChildren[i]);
			}

			nodeChildren.ClearFast();
		}

		void JobRecalculate () {
			Profiler.BeginSample("Recalculate Connected Components");

			for (int i = 0; i < numDirtyNodes; i++) {
				dirty[dirtyNodes[i].HierarchicalNodeIndex] = 1;
			}

			Profiler.BeginSample("Remove");
			// Remove all hierarchical nodes and then build new hierarchical nodes in their place
			// which take into account the new graph data.
			for (int i = 1; i < dirty.Length; i++) {
				if (dirty[i] == 1) RemoveHierarchicalNode(i, true);
			}
			for (int i = 1; i < dirty.Length; i++) dirty[i] = 0;

			for (int i = 0; i < numDirtyNodes; i++) {
				dirtyNodes[i].HierarchicalNodeIndex = 0;
			}
			Profiler.EndSample();

			Profiler.BeginSample("Find");
			for (int i = 0; i < numDirtyNodes; i++) {
				var node = dirtyNodes[i];
				// Be nice to the GC
				dirtyNodes[i] = null;
				node.IsHierarchicalNodeDirty = false;

				if (node.HierarchicalNodeIndex == 0 && node.Walkable && !node.Destroyed) {
					FindHierarchicalNodeChildren(GetHierarchicalNodeIndex(), node);
				}
			}
			Profiler.EndSample();

			numDirtyNodes = 0;
			// Recalculate the connected components of the hierarchical nodes
			// This is usually very quick compared to the code above
			FloodFill();
			Profiler.EndSample();
			gizmoVersion++;
		}

		/// <summary>Recalculate the hierarchical graph and the connected components if any nodes have been marked as dirty</summary>
		public void RecalculateIfNecessary () {
			lastJob.Complete();
			if (numDirtyNodes > 0) JobRecalculate();
		}

		/// <summary>
		/// Schedule a job to recalculate the hierarchical graph and the connected components if any nodes have been marked as dirty.
		/// Returns dependsOn if nothing has to be done.
		/// </summary>
		public JobHandle JobRecalculateIfNecessary (JobHandle dependsOn = default) {
			if (numDirtyNodes > 0) {
				lastJob = Pathfinding.Jobs.IJobExtensions.ScheduleManaged(JobRecalculate, JobHandle.CombineDependencies(lastJob, dependsOn));
				return lastJob;
			} else {
				return dependsOn;
			}
		}

		/// <summary>
		/// Recalculate everything from scratch.
		/// This is primarily to be used for legacy code for compatibility reasons, not for any new code.
		///
		/// See: <see cref="RecalculateIfNecessary"/>
		/// </summary>
		public void RecalculateAll () {
			AstarPath.active.data.GetNodes(AddDirtyNode);
			RecalculateIfNecessary();
		}

		/// <summary>Flood fills the graph of hierarchical nodes and assigns the same area ID to all hierarchical nodes that are in the same connected component</summary>
		void FloodFill () {
			for (int i = 0; i < areas.Length; i++) areas[i] = 0;

			Stack<int> stack = temporaryStack;
			int currentArea = 0;
			for (int i = 1; i < areas.Length; i++) {
				// Already taken care of
				if (areas[i] != 0) continue;

				currentArea++;
				areas[i] = currentArea;
				stack.Push(i);
				while (stack.Count > 0) {
					int node = stack.Pop();
					var conns = connections[node];
					for (int j = conns.Count - 1; j >= 0; j--) {
						var otherNode = conns[j];
						// Note: slightly important that this is != currentArea and not != 0 in case there are some connected, but not stongly connected components in the graph (this will happen in only veeery few types of games)
						if (areas[otherNode] != currentArea) {
							areas[otherNode] = currentArea;
							stack.Push(otherNode);
						}
					}
				}
			}

			NumConnectedComponents = System.Math.Max(1, currentArea + 1);
			version++;
		}

		/// <summary>Run a BFS out from a start node and assign up to MaxChildrenPerNode nodes to the specified hierarchical node which are not already assigned to another hierarchical node</summary>
		void FindHierarchicalNodeChildren (int hierarchicalNode, GraphNode startNode) {
			// Set some state for the connectionCallback delegate to use
			currentChildren = children[hierarchicalNode];
			currentConnections = connections[hierarchicalNode];
			currentHierarchicalNodeIndex = hierarchicalNode;

			var que = temporaryQueue;
			que.Enqueue(startNode);

			startNode.HierarchicalNodeIndex = hierarchicalNode;
			currentChildren.Add(startNode);

			while (que.Count > 0) {
				que.Dequeue().GetConnections(connectionCallback);
			}

			for (int i = 0; i < currentConnections.Count; i++) {
				connections[currentConnections[i]].Add(hierarchicalNode);
			}

			que.Clear();
		}

		public void OnDrawGizmos (DrawingData gizmos, RedrawScope redrawScope) {
			var hasher = new NodeHasher(AstarPath.active);

			hasher.Add(gizmoVersion);

			if (!gizmos.Draw(hasher, redrawScope)) {
				using (var builder = gizmos.GetBuilder(hasher, redrawScope)) {
					var centers = ArrayPool<Vector3>.Claim(areas.Length);
					for (int i = 0; i < areas.Length; i++) {
						Int3 center = Int3.zero;
						var childs = children[i];
						if (childs.Count > 0) {
							for (int j = 0; j < childs.Count; j++) center += childs[j].position;
							center /= childs.Count;
							centers[i] = (Vector3)center;
						}
					}

					for (int i = 0; i < areas.Length; i++) {
						if (children[i].Count > 0) {
							for (int j = 0; j < connections[i].Count; j++) {
								if (connections[i][j] > i) {
									builder.Line(centers[i], centers[connections[i][j]], Color.black);
								}
							}
						}
					}
				}
			}
		}
	}
}
