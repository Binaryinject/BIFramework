using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

namespace Pathfinding {
	using Pathfinding.Util;

	/// <summary>
	/// Implements the funnel algorithm as well as various related methods.
	/// See: http://digestingduck.blogspot.se/2010/03/simple-stupid-funnel-algorithm.html
	/// See: Usually you do not use this class directly. Instead use the <see cref="FunnelModifier"/> component.
	///
	/// <code>
	/// using UnityEngine;
	/// using Pathfinding;
	/// using Pathfinding.Drawing;
	///
	/// public class FunnelExample : MonoBehaviour {
	///     public Transform target = null;
	///
	///     void Update () {
	///         var path = ABPath.Construct(transform.position, target.position);
	///
	///         AstarPath.StartPath(path);
	///         path.BlockUntilCalculated();
	///
	///         // Apply some default adjustments to the path
	///         // not necessary if you are using the Seeker component
	///         new StartEndModifier().Apply(path);
	///
	///         // Split the path into segments and links
	///         var parts = Funnel.SplitIntoParts(path);
	///         // Optionally simplify the path to make it straighter
	///         var nodes = path.path;
	///         Funnel.Simplify(parts, ref nodes);
	///
	///         using (Draw.WithLineWidth(2)) {
	///             // Go through all the parts and draw them in the scene view
	///             for (int i = 0; i < parts.Count; i++) {
	///                 var part = parts[i];
	///                 if (part.type == Funnel.PartType.OffMeshLink) {
	///                     // Draw off-mesh links as a single line
	///                     Draw.Line(part.startPoint, part.endPoint, Color.cyan);
	///                 } else {
	///                     // Calculate the shortest path through the funnel
	///                     var portals = Funnel.ConstructFunnelPortals(nodes, part);
	///                     var pathThroghPortals = Funnel.Calculate(portals, splitAtEveryPortal: false);
	///                     Draw.Polyline(pathThroghPortals, Color.black);
	///                 }
	///             }
	///         }
	///     }
	/// }
	/// </code>
	///
	/// In the image you can see the output from the code example above. The cyan lines represent off-mesh links.
	///
	/// [Open online documentation to see images]
	/// </summary>
	public class Funnel {
		/// <summary>Funnel in which the path to the target will be</summary>
		public struct FunnelPortals {
			public List<Vector3> left;
			public List<Vector3> right;
		}

		/// <summary>The type of a <see cref="PathPart"/></summary>
		public enum PartType {
			/// <summary>An off-mesh link between two nodes in the same or different graphs</summary>
			OffMeshLink,
			/// <summary>A sequence of adjacent nodes in the same graph</summary>
			NodeSequence,
		}

		/// <summary>
		/// Part of a path.
		/// This is either a sequence of adjacent triangles
		/// or a link.
		/// See: NodeLink2
		/// </summary>
		public struct PathPart {
			/// <summary>Index of the first node in this part</summary>
			public int startIndex;
			/// <summary>Index of the last node in this part</summary>
			public int endIndex;
			/// <summary>Exact start-point of this part or off-mesh link</summary>
			public Vector3 startPoint;
			/// <summary>Exact end-point of this part or off-mesh link</summary>
			public Vector3 endPoint;
			/// <summary>If this is an off-mesh link or a sequence of nodes in a single graph</summary>
			public PartType type;
		}

		/// <summary>Splits the path into a sequence of parts which are either off-mesh links or sequences of adjacent triangles</summary>

		public static List<PathPart> SplitIntoParts (Path path) {
			var nodes = path.path;

			var result = ListPool<PathPart>.Claim();

			if (nodes == null || nodes.Count == 0) {
				return result;
			}

			// Loop through the path and split it into
			// parts joined by links
			for (int i = 0; i < nodes.Count; i++) {
				var node = nodes[i];
				if (node is TriangleMeshNode || node is GridNodeBase) {
					var startIndex = i;
					uint currentGraphIndex = node.GraphIndex;

					// Loop up until we find a node in another graph
					// Ignore NodeLink3 nodes
					while (i < nodes.Count && (nodes[i].GraphIndex == currentGraphIndex || nodes[i] is NodeLink3Node)) i++;

					i--;
					var endIndex = i;
					result.Add(new PathPart {
						type = PartType.NodeSequence,
						startIndex = startIndex,
						endIndex = endIndex,
						// If this is the first part in the path, use the exact start point
						// otherwise use the position of the node right before the start of this
						// part which is likely the end of the link to this part
						startPoint = startIndex == 0 ? path.vectorPath[0] : (Vector3)nodes[startIndex-1].position,
						endPoint = endIndex == nodes.Count-1 ? path.vectorPath[path.vectorPath.Count-1] : (Vector3)nodes[endIndex+1].position,
					});
				} else if (NodeLink2.GetNodeLink(node) != null) {
					var startIndex = i;
					var currentGraphIndex = node.GraphIndex;


					for (i++; i < nodes.Count; i++) {
						if (nodes[i].GraphIndex != currentGraphIndex) {
							break;
						}
					}
					i--;

					if (i - startIndex == 0) {
						// Just ignore it, it might be the case that a NodeLink was the closest node
						continue;
					} else if (i - startIndex != 1) {
						throw new System.Exception("NodeLink2 link length greater than two (2) nodes. " + (i - startIndex + 1));
					}

					result.Add(new PathPart {
						type = Funnel.PartType.OffMeshLink,
						startIndex = startIndex,
						endIndex = i,
						startPoint = (Vector3)nodes[startIndex].position,
						endPoint = (Vector3)nodes[i].position,
					});
				} else {
					throw new System.Exception("Unsupported node type or null node");
				}
			}

			return result;
		}


		public static void Simplify (List<PathPart> parts, ref List<GraphNode> nodes) {
			List<GraphNode> resultNodes = ListPool<GraphNode>.Claim();

			for (int i = 0; i < parts.Count; i++) {
				var part = parts[i];

				// We are changing the nodes list, so indices may change
				var newPart = part;
				newPart.startIndex = resultNodes.Count;

				if (part.type == PartType.NodeSequence) {
					var graph = nodes[part.startIndex].Graph as IRaycastableGraph;
					if (graph != null) {
						Simplify(part, graph, nodes, resultNodes, Path.ZeroTagPenalties, -1);
						newPart.endIndex = resultNodes.Count - 1;
						parts[i] = newPart;
						continue;
					}
				}

				for (int j = part.startIndex; j <= part.endIndex; j++) {
					resultNodes.Add(nodes[j]);
				}
				newPart.endIndex = resultNodes.Count - 1;
				parts[i] = newPart;
			}

			ListPool<GraphNode>.Release(ref nodes);
			nodes = resultNodes;
		}

		/// <summary>
		/// Simplifies a funnel path using linecasting.
		/// Running time is roughly O(n^2 log n) in the worst case (where n = end-start)
		/// Actually it depends on how the graph looks, so in theory the actual upper limit on the worst case running time is O(n*m log n) (where n = end-start and m = nodes in the graph)
		/// but O(n^2 log n) is a much more realistic worst case limit.
		///
		/// Requires <see cref="graph"/> to implement IRaycastableGraph
		/// </summary>
		public static void Simplify (PathPart part, IRaycastableGraph graph, List<GraphNode> nodes, List<GraphNode> result, int[] tagPenalties, int traversableTags) {
			var start = part.startIndex;
			var end = part.endIndex;
			var startPoint = part.startPoint;
			var endPoint = part.endPoint;

			if (graph == null) throw new System.ArgumentNullException(nameof(graph));

			if (start > end) {
				throw new System.ArgumentException("start > end");
			}

			// Do a straight line of sight check to see if the path can be simplified to a single line
			{
				GraphHitInfo hit;
				if (!graph.Linecast(startPoint, endPoint, out hit) && hit.node == nodes[end]) {
					graph.Linecast(startPoint, endPoint, out hit, result);

					long penaltySum = 0;
					long penaltySum2 = 0;
					for (int i = start; i <= end; i++) {
						penaltySum += nodes[i].Penalty + tagPenalties[nodes[i].Tag];
					}

					bool walkable = true;
					for (int i = 0; i < result.Count; i++) {
						penaltySum2 += result[i].Penalty + tagPenalties[result[i].Tag];
						walkable &= ((traversableTags >> (int)result[i].Tag) & 1) == 1;
					}

					// Allow 40% more penalty on average per node
					if (!walkable || (penaltySum*1.4*result.Count) < (penaltySum2*(end-start+1))) {
						// The straight line penalties are much higher than the original path.
						// Revert the simplification
						result.Clear();
					} else {
						// The straight line simplification looks good.
						// We are done here.
						return;
					}
				}
			}

			int ostart = start;

			int count = 0;
			while (true) {
				if (count++ > 1000) {
					Debug.LogError("Was the path really long or have we got cought in an infinite loop?");
					break;
				}

				if (start == end) {
					result.Add(nodes[end]);
					return;
				}

				int resCount = result.Count;

				// Run a binary search to find the furthest node that we have a clear line of sight to
				int mx = end+1;
				int mn = start+1;
				bool anySucceded = false;
				while (mx > mn+1) {
					int mid = (mx+mn)/2;

					GraphHitInfo hit;
					Vector3 sp = start == ostart ? startPoint : (Vector3)nodes[start].position;
					Vector3 ep = mid == end ? endPoint : (Vector3)nodes[mid].position;

					// Check if there is an obstacle between these points, or if there is no obstacle, but we didn't end up at the right node.
					// The second case can happen for example in buildings with multiple floors.
					if (graph.Linecast(sp, ep, out hit) || hit.node != nodes[mid]) {
						mx = mid;
					} else {
						anySucceded = true;
						mn = mid;
					}
				}

				if (!anySucceded) {
					result.Add(nodes[start]);

					// It is guaranteed that mn = start+1
					start = mn;
				} else {
					// Replace a part of the path with the straight path to the furthest node we had line of sight to.
					// Need to redo the linecast to get the trace (i.e. list of nodes along the line of sight).
					GraphHitInfo hit;
					Vector3 sp = start == ostart ? startPoint : (Vector3)nodes[start].position;
					Vector3 ep = mn == end ? endPoint : (Vector3)nodes[mn].position;
					graph.Linecast(sp, ep, out hit, result);

					long penaltySum = 0;
					long penaltySum2 = 0;
					for (int i = start; i <= mn; i++) {
						penaltySum += nodes[i].Penalty + tagPenalties[nodes[i].Tag];
					}

					bool walkable = true;
					for (int i = resCount; i < result.Count; i++) {
						penaltySum2 += result[i].Penalty + tagPenalties[result[i].Tag];
						walkable &= ((traversableTags >> (int)result[i].Tag) & 1) == 1;
					}

					// Allow 40% more penalty on average per node
					if (!walkable || (penaltySum*1.4*(result.Count-resCount)) < (penaltySum2*(mn-start+1)) || result[result.Count-1] != nodes[mn]) {
						//Debug.DrawLine ((Vector3)nodes[start].Position, (Vector3)nodes[mn].Position, Color.red);
						// Linecast hit the wrong node or it is a lot more expensive than the original path
						result.RemoveRange(resCount, result.Count-resCount);

						result.Add(nodes[start]);
						//Debug.Break();
						start = start+1;
					} else {
						//Debug.DrawLine ((Vector3)nodes[start].Position, (Vector3)nodes[mn].Position, Color.green);
						//Remove nodes[end]
						result.RemoveAt(result.Count-1);
						start = mn;
					}
				}
			}
		}

		public static FunnelPortals ConstructFunnelPortals (List<GraphNode> nodes, PathPart part) {
			if (nodes == null || nodes.Count == 0) {
				return new FunnelPortals { left = ListPool<Vector3>.Claim(0), right = ListPool<Vector3>.Claim(0) };
			}

			if (part.endIndex < part.startIndex || part.startIndex < 0 || part.endIndex > nodes.Count) throw new System.ArgumentOutOfRangeException();

			// Claim temporary lists and try to find lists with a high capacity
			var left = ListPool<Vector3>.Claim(nodes.Count+1);
			var right = ListPool<Vector3>.Claim(nodes.Count+1);

			// Add start point
			left.Add(part.startPoint);
			right.Add(part.startPoint);

			// Loop through all nodes in the path (except the last one)
			for (int i = part.startIndex; i < part.endIndex; i++) {
				// Get the portal between path[i] and path[i+1] and add it to the left and right lists
				bool portalWasAdded = nodes[i].GetPortal(nodes[i+1], left, right, false);

				if (!portalWasAdded) {
					// Fallback, just use the positions of the nodes
					left.Add((Vector3)nodes[i].position);
					right.Add((Vector3)nodes[i].position);

					left.Add((Vector3)nodes[i+1].position);
					right.Add((Vector3)nodes[i+1].position);
				}
			}

			// Add end point
			left.Add(part.endPoint);
			right.Add(part.endPoint);

			return new FunnelPortals { left = left, right = right };
		}

		public struct FunnelState {
			public CircularBuffer<Vector3> leftFunnel;
			public CircularBuffer<Vector3> rightFunnel;
			/// <summary>
			/// Unwrapped version of the funnel portals in 2D space.
			///
			/// The input is a funnel like in the image below. It may be rotated and twisted.
			/// [Open online documentation to see images]
			/// The output will be a funnel in 2D space like in the image below. All twists and bends will have been straightened out.
			/// [Open online documentation to see images]
			/// </summary>
			public CircularBuffer<Vector2> leftUnwrappedFunnel;
			/// <summary>Like <see cref="leftUnwrappedFunnel"/> but for the right side of the funnel</summary>
			public CircularBuffer<Vector2> rightUnwrappedFunnel;

			public FunnelState(int initialCapacity) {
				leftFunnel = new CircularBuffer<Vector3>(initialCapacity);
				rightFunnel = new CircularBuffer<Vector3>(initialCapacity);
				leftUnwrappedFunnel = new CircularBuffer<Vector2>(initialCapacity);
				rightUnwrappedFunnel = new CircularBuffer<Vector2>(initialCapacity);
			}

			public FunnelState(FunnelPortals portals) : this(portals.left.Count) {
				if (portals.left.Count != portals.right.Count) throw new System.ArgumentException("portals.left.Count != portals.right.Count");
				for (int i = 0; i < portals.left.Count; i++) {
					PushEnd(portals.left[i], portals.right[i]);
				}
			}

			public void Clear () {
				leftFunnel.Clear();
				rightFunnel.Clear();
				leftUnwrappedFunnel.Clear();
				rightUnwrappedFunnel.Clear();
			}

			public void PopStart () {
				leftFunnel.PopStart();
				rightFunnel.PopStart();
				leftUnwrappedFunnel.PopStart();
				rightUnwrappedFunnel.PopStart();
			}

			public void PopEnd () {
				leftFunnel.PopEnd();
				rightFunnel.PopEnd();
				leftUnwrappedFunnel.PopEnd();
				rightUnwrappedFunnel.PopEnd();
			}

			static Vector2 Unwrap (Vector3 leftPortal, Vector3 rightPortal, Vector2 leftUnwrappedPortal, Vector2 rightUnwrappedPortal, Vector3 point, float sideMultiplier) {
				var portal = rightPortal - leftPortal;
				var portalLengthInvSq = 1.0f / portal.sqrMagnitude;
				if (float.IsPositiveInfinity(portalLengthInvSq)) {
					return leftUnwrappedPortal + new Vector2(-(point - leftPortal).magnitude, 0);
				}
				var distance = Vector3.Cross(point - leftPortal, portal).magnitude * portalLengthInvSq;
				var projection = Vector3.Dot(point - leftPortal, portal) * portalLengthInvSq;
				var unwrappedPortal = rightUnwrappedPortal - leftUnwrappedPortal;
				var unwrappedNormal = new Vector2(-unwrappedPortal.y, unwrappedPortal.x);
				return leftUnwrappedPortal + unwrappedPortal * projection + unwrappedNormal * (distance * sideMultiplier);
			}

			void Init (Vector3 newLeftPortal, Vector3 newRightPortal) {
				leftFunnel.PushEnd(newLeftPortal);
				rightFunnel.PushEnd(newRightPortal);
				leftUnwrappedFunnel.PushEnd(Vector2.zero);
				rightUnwrappedFunnel.PushEnd(new Vector2((newRightPortal - newLeftPortal).magnitude, 0));
			}

			public void PushStart (Vector3 newLeftPortal, Vector3 newRightPortal) {
				if (leftFunnel.Length == 0) {
					Init(newLeftPortal, newRightPortal);
					return;
				}
				leftUnwrappedFunnel.PushStart(Unwrap(leftFunnel.First, rightFunnel.First, leftUnwrappedFunnel.First, rightUnwrappedFunnel.First, newLeftPortal, -1));
				leftFunnel.PushStart(newLeftPortal);

				rightUnwrappedFunnel.PushStart(Unwrap(leftFunnel.First, rightFunnel.First, leftUnwrappedFunnel.First, rightUnwrappedFunnel.First, newRightPortal, -1));
				rightFunnel.PushStart(newRightPortal);
			}

			public void PushEnd (Vector3 newLeftPortal, Vector3 newRightPortal) {
				if (leftFunnel.Length == 0) {
					Init(newLeftPortal, newRightPortal);
					return;
				}
				var last = leftFunnel.Length-1;
				leftUnwrappedFunnel.PushEnd(Unwrap(leftFunnel.Last, rightFunnel.Last, leftUnwrappedFunnel.Last, rightUnwrappedFunnel.Last, newLeftPortal, 1));
				leftFunnel.PushEnd(newLeftPortal);

				rightUnwrappedFunnel.PushEnd(Unwrap(leftFunnel.Last, rightFunnel.Last, leftUnwrappedFunnel.Last, rightUnwrappedFunnel.Last, newRightPortal, 1));
				rightFunnel.PushEnd(newRightPortal);
			}

			public void Push (bool toStart, Vector3 newLeftPortal, Vector3 newRightPortal) {
				if (toStart) PushStart(newLeftPortal, newRightPortal);
				else PushEnd(newLeftPortal, newRightPortal);
			}

			public void Pool () {
				leftFunnel.Pool();
				rightFunnel.Pool();
				leftUnwrappedFunnel.Pool();
				rightUnwrappedFunnel.Pool();
			}

			public List<Vector3> CalculateNextCorners (int maxCorners, bool splitAtEveryPortal, List<Vector3> result) {
				var intermediateResult = ListPool<int>.Claim();
				leftUnwrappedFunnel.MakeContiguous(out var leftArr, out int startIndex);
				rightUnwrappedFunnel.MakeContiguous(out var rightArr, out int rightStartIndex);
				UnityEngine.Assertions.Assert.AreEqual(startIndex, rightStartIndex);
				UnityEngine.Assertions.Assert.IsTrue(leftArr.Length - startIndex >= 2);

				Calculate(leftArr, rightArr, startIndex, startIndex + leftUnwrappedFunnel.Length - 1, intermediateResult, maxCorners, out bool lastCorner);

				if (result.Capacity < intermediateResult.Count) result.Capacity = intermediateResult.Count;

				Vector2 prev2D = leftArr[startIndex];
				var prevIdx = 0;
				for (int i = 0; i < intermediateResult.Count; i++) {
					var idx = intermediateResult[i];

					if (splitAtEveryPortal) {
						// Check intersections with every portal segment
						var next2D = idx >= 0 ? leftArr[idx] : rightArr[-idx];
						for (int j = prevIdx + 1; j < System.Math.Abs(idx); j++) {
							if (!VectorMath.LineLineIntersectionFactor(leftArr[j], rightArr[j] - leftArr[j], prev2D, next2D - prev2D, out float factor)) {
								// This really shouldn't happen
								factor = 0.5f;
							}
							result.Add(Vector3.Lerp(leftFunnel[j - startIndex], rightFunnel[j - startIndex], factor));
						}

						prevIdx = Mathf.Abs(idx);
						prev2D = next2D;
					}

					if (idx >= 0) {
						result.Add(leftFunnel[idx - startIndex]);
					} else {
						result.Add(rightFunnel[-idx - startIndex]);
					}
				}
				// Release lists back to the pool
				ListPool<int>.Release(ref intermediateResult);
				return result;
			}
		}

		/// <summary>True if b is to the right of or on the line from (0,0) to a</summary>
		protected static bool RightOrColinear (Vector2 a, Vector2 b) {
			return (a.x*b.y - b.x*a.y) <= 0;
		}

		/// <summary>True if b is to the left of or on the line from (0,0) to a</summary>
		protected static bool LeftOrColinear (Vector2 a, Vector2 b) {
			return (a.x*b.y - b.x*a.y) >= 0;
		}

		/// <summary>
		/// Calculate the shortest path through the funnel.
		///
		/// The path will be unwrapped into 2D space before the funnel algorithm runs.
		/// This makes it possible to support the funnel algorithm in XY space as well as in more complicated cases, such as on curved worlds.
		/// [Open online documentation to see images]
		///
		/// [Open online documentation to see images]
		///
		/// See: Unwrap
		/// </summary>
		/// <param name="funnel">The portals of the funnel. The first and last vertices portals must be single points (so for example left[0] == right[0]).</param>
		/// <param name="splitAtEveryPortal">If true, then a vertex will be inserted every time the path crosses a portal
		///  instead of only at the corners of the path. The result will have exactly one vertex per portal if this is enabled.
		///  This may introduce vertices with the same position in the output (esp. in corners where many portals meet).</param>
		public static List<Vector3> Calculate (FunnelPortals funnel, bool splitAtEveryPortal) {
			var state = new FunnelState(funnel);
			var result = ListPool<Vector3>.Claim();
			state.CalculateNextCorners(int.MaxValue, splitAtEveryPortal, result);
			state.Pool();
			return result;
		}

		/// <summary>
		/// Funnel algorithm.
		/// funnelPath will be filled with the result.
		/// The result is the indices of the vertices that were picked, a non-negative value refers to the corresponding index in the
		/// left array, a negative value refers to the corresponding index in the right array.
		/// So e.g 5 corresponds to left[5] and -2 corresponds to right[2]
		///
		/// See: http://digestingduck.blogspot.se/2010/03/simple-stupid-funnel-algorithm.html
		/// </summary>
		public static void Calculate (Vector2[] left, Vector2[] right, int startIndex, int endIndex, List<int> funnelPath, int maxCorners, out bool lastCorner) {
			if (left.Length != right.Length) throw new System.ArgumentException();

			lastCorner = false;

			int apexIndex = startIndex + 0;
			int rightIndex = startIndex + 1;
			int leftIndex = startIndex + 1;

			Vector2 portalApex = left[apexIndex];
			Vector2 portalLeft = left[leftIndex];
			Vector2 portalRight = right[rightIndex];

			funnelPath.Add(apexIndex);

			for (int i = startIndex + 2; i <= endIndex; i++) {
				if (funnelPath.Count >= maxCorners) {
					return;
				}

				if (funnelPath.Count > 2000) {
					Debug.LogWarning("Avoiding infinite loop. Remove this check if you have this long paths.");
					break;
				}

				Vector2 pLeft = left[i];
				Vector2 pRight = right[i];

				if (LeftOrColinear(portalRight - portalApex, pRight - portalApex)) {
					if (portalApex == portalRight || RightOrColinear(portalLeft - portalApex, pRight - portalApex)) {
						portalRight = pRight;
						rightIndex = i;
					} else {
						portalApex = portalRight = portalLeft;
						i = apexIndex = rightIndex = leftIndex;

						funnelPath.Add(apexIndex);
						continue;
					}
				}

				if (RightOrColinear(portalLeft - portalApex, pLeft - portalApex)) {
					if (portalApex == portalLeft || LeftOrColinear(portalRight - portalApex, pLeft - portalApex)) {
						portalLeft = pLeft;
						leftIndex = i;
					} else {
						portalApex = portalLeft = portalRight;
						i = apexIndex = leftIndex = rightIndex;

						// Negative value because we are referring
						// to the right side
						funnelPath.Add(-apexIndex);

						continue;
					}
				}
			}

			lastCorner = true;
			funnelPath.Add(endIndex);
		}
	}
}
