using System.Collections.Generic;
using UnityEngine;
using static Pathfinding.Funnel;
using Pathfinding.Util;
using UnityEngine.Assertions;

namespace Pathfinding {
	public struct PathTracer {
		List<PathPart> parts;
		CircularBuffer<GraphNode> nodes;
		Funnel.FunnelState funnelState;

		int firstPartIndex;
		bool startIsUpToDate;
		bool endIsUpToDate;

		public Vector3 UpdateStart (Vector3 position) {
			Repair(position, true);
			return parts[firstPartIndex].startPoint;
		}
		public Vector3 UpdateEnd (Vector3 position) {
			Repair(position, false);
			return parts[parts.Count-1].endPoint;
		}

		void AppendPath (bool toStart, CircularBuffer<GraphNode> path) {
			Assert.AreEqual(parts[parts.Count-1].endIndex, nodes.AbsoluteEndIndex);
			var partIndex = toStart ? this.firstPartIndex : this.parts.Count - 1;
			var part = parts[partIndex];
			// Remove any shared prefix of the repair path and the current path
			while (path.Length > 0 && part.startIndex <= part.endIndex && path[0] == nodes.GetBoundaryValue(toStart)) {
				// Don't pop the last node from the repair path
				if (path.Length > 1) path.PopStart();
				if (toStart) {
					part.startIndex++;
				} else {
					part.endIndex--;
				}
				nodes.Pop(toStart);
				if (partIndex == this.firstPartIndex && funnelState.leftFunnel.Length > 0) funnelState.PopStart();
			}

			// Insert any additional nodes into the path
			if (toStart) {
				part.startIndex -= path.Length;
			} else {
				part.endIndex += path.Length;
			}
			if (partIndex == this.firstPartIndex) {
				var prevNode = nodes.GetBoundaryValue(toStart);
				var tmpLeft = new List<Vector3>();
				var tmpRight = new List<Vector3>();
				for (int i = path.Length - 1; i >= 0; i--) {
					var node = path[i];
					tmpLeft.Clear();
					tmpRight.Clear();
					if (!node.GetPortal(prevNode, tmpLeft, tmpRight, false)) {
						throw new System.NotImplementedException();
					}
					funnelState.Push(toStart, tmpLeft[0], tmpRight[0]);
					prevNode = path[i];
				}
			}

			while (path.Length > 0) {
				nodes.Push(path.PopStart(), toStart);
			}
			parts[partIndex] = part;

			Assert.AreEqual(parts[parts.Count-1].endIndex, nodes.AbsoluteEndIndex);
			Assert.AreEqual(parts[this.firstPartIndex].startIndex, nodes.AbsoluteStartIndex);
			Assert.IsTrue(part.endIndex >= part.startIndex);
		}

		void Repair (Vector3 point, bool isStart) {
			// TODO: Rewrite to
			// 1. Take a movement plane
			// 2. Use Int3 coordinates everywhere
			const int MAX_NODES_TO_SEARCH = 16;
			var queue = ArrayPool<GraphNode>.Claim(MAX_NODES_TO_SEARCH);
			var parents = ArrayPool<int>.Claim(MAX_NODES_TO_SEARCH);
			var queueHead = 0;
			var queueTail = 0;
			var partIndex = isStart ? this.firstPartIndex : this.parts.Count - 1;
			var currentNode = nodes[isStart ? parts[partIndex].startIndex : parts[partIndex].endIndex];
			parents[queueTail] = -1;
			queue[queueTail++] = currentNode;
			int nodesSearched = 0;
			while (queueHead < queueTail) {
				var nodeQueueIndex = queueHead;
				var node = queue[queueHead++];
				nodesSearched++;
				if (node is MeshNode mNode && mNode.ContainsPoint(point)) {
					// Trace the repair path back to connect to the current path
					var repairPath = new CircularBuffer<GraphNode>(8);
					while (true) {
						repairPath.PushStart(queue[nodeQueueIndex]);
						if (parents[nodeQueueIndex] != -1) {
							nodeQueueIndex = parents[nodeQueueIndex];
						} else {
							break;
						}
					}
					AppendPath(isStart, repairPath);
					repairPath.Pool();
					break;
				} else {
					var closest = node.ClosestPointOnNode(point);

					// Check if the neighbour nodes are closer than the parent node.
					// Allow for a small margin to both avoid floating point errors and to allow
					// moving past very small local minima.
					var distanceThresholdSqr = (point - closest).sqrMagnitude*1.05f*1.05f + 0.05f;
					node.GetConnections(neighbour => {
						if (queueTail < MAX_NODES_TO_SEARCH && neighbour.GraphIndex == node.GraphIndex) {
							var closestOnNeighbour = neighbour.ClosestPointOnNode(point);
							var distanceToNeighbourSqr = (point - closestOnNeighbour).sqrMagnitude;
							if (distanceToNeighbourSqr < distanceThresholdSqr && System.Array.IndexOf(queue, neighbour, 0, queueTail) == -1) {
								queue[queueTail] = neighbour;
								parents[queueTail] = nodeQueueIndex;
								queueTail++;
							}
						}
					});
				}
			}
			ArrayPool<GraphNode>.Release(ref queue);
			ArrayPool<int>.Release(ref parents);
			{
				var part = parts[partIndex];
				var clampedPoint = isStart ? part.startPoint : part.endPoint;
				// TODO: xzDistance
				var globallyClosestNode = AstarPath.active.GetNearest(point);
				// true => Ok. The node we have found is as good, or almost as good as the actually closest node
				// false => Bad. We didn't find the best node when repairing. We may need to recalculate our path.
				var upToDate = (point - clampedPoint).sqrMagnitude < (point - globallyClosestNode.position).sqrMagnitude + 0.1f*0.1f;
				if (isStart) {
					part.startPoint = nodes.GetAbsolute(part.startIndex).ClosestPointOnNode(point);
					startIsUpToDate = upToDate;
				} else {
					part.endPoint = nodes.GetAbsolute(part.endIndex).ClosestPointOnNode(point);
					endIsUpToDate = upToDate;
				}
				parts[partIndex] = part;
			}
		}
		public GraphNode startNode => nodes.GetAbsolute(parts[firstPartIndex].startIndex);
		// TODO: endIsUpToDate may end up always being false when the path is a partial path
		public bool isStale => !endIsUpToDate || !startIsUpToDate;
		public int partCount => parts.Count - firstPartIndex;
		public void GetNextCorners (List<Vector3> buffer, int maxCorners) {
			var part = parts[firstPartIndex];
			funnelState.PushStart(part.startPoint, part.startPoint);
			funnelState.PushEnd(part.endPoint, part.endPoint);
			try {
				funnelState.CalculateNextCorners(maxCorners, false, buffer);
			} finally {
				funnelState.PopStart();
				funnelState.PopEnd();
			}
		}
		public void PopFirstPart () {
			if (firstPartIndex >= parts.Count - 1) throw new System.InvalidOperationException("Cannot pop the last part of a path");
			firstPartIndex++;
		}
		public PartType GetPartType (int partIndex = 0) {
			return parts[this.firstPartIndex + partIndex].type;
		}
		public void GetCurrentLinkInfo (int partIndex = 0) {}
		public void SetPath (List<PathPart> parts, List<GraphNode> nodes) {
			this.parts = parts;
			this.nodes.Clear();
			this.nodes.AddRange(nodes);
			this.funnelState.Clear();
			this.firstPartIndex = 0;
			var part = parts[0];
			var prevNode = this.nodes.GetAbsolute(part.startIndex);
			var tmpLeft = new List<Vector3>();
			var tmpRight = new List<Vector3>();
			for (int i = part.startIndex + 1; i <= part.endIndex; i++) {
				var node = this.nodes.GetAbsolute(i);
				tmpLeft.Clear();
				tmpRight.Clear();
				if (prevNode.GetPortal(node, tmpLeft, tmpRight, false)) {
					this.funnelState.PushEnd(tmpLeft[0], tmpRight[0]);
				} else {
					Debug.Log("Couldn't find a portal from " + prevNode + " " + node + " " + prevNode.ContainsConnection(node));
					throw new System.NotImplementedException();
				}
				prevNode = node;
			}
		}
	}
}
