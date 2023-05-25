using UnityEngine;

namespace Pathfinding.Util {
	using Pathfinding.Drawing;

	/// <summary>Combines hashes into a single hash value</summary>
	public struct NodeHasher {
		bool includePathSearchInfo;
		bool includeAreaInfo;
		PathHandler debugData;
		public DrawingData.Hasher hasher;

		public NodeHasher(AstarPath active) {
			hasher = default;
			this.debugData = active.debugPathData;
			includePathSearchInfo = debugData != null && (active.debugMode == GraphDebugMode.F || active.debugMode == GraphDebugMode.G || active.debugMode == GraphDebugMode.H || active.showSearchTree);
			includeAreaInfo = active.debugMode == GraphDebugMode.Areas;
			hasher.Add(active.debugMode);
			hasher.Add(active.debugFloor);
			hasher.Add(active.debugRoof);
			hasher.Add(AstarColor.ColorHash());
		}

		public void HashNode (GraphNode node) {
			hasher.Add(node.GetGizmoHashCode());
			if (includeAreaInfo) hasher.Add((int)node.Area);

			if (includePathSearchInfo) {
				var pathNode = debugData.GetPathNode(node.NodeIndex);
				hasher.Add(pathNode.pathID);
				hasher.Add(pathNode.pathID == debugData.PathID);
				hasher.Add(pathNode.F);
			}
		}

		public void Add<T>(T v) {
			hasher.Add(v);
		}

		public static implicit operator DrawingData.Hasher(NodeHasher hasher) {
			return hasher.hasher;
		}
	}

	public class GraphGizmoHelper : IAstarPooledObject, System.IDisposable {
		public DrawingData.Hasher hasher { get; private set; }
		PathHandler debugData;
		ushort debugPathID;
		GraphDebugMode debugMode;
		bool showSearchTree;
		float debugFloor;
		float debugRoof;
		public CommandBuilder builder;
		Vector3 drawConnectionStart;
		Color drawConnectionColor;
		readonly System.Action<GraphNode> drawConnection;

		public GraphGizmoHelper () {
			// Cache a delegate to avoid allocating memory for it every time
			drawConnection = DrawConnection;
		}

		public static GraphGizmoHelper GetSingleFrameGizmoHelper (DrawingData gizmos, AstarPath active, RedrawScope redrawScope) {
			return GetGizmoHelper(gizmos, active, DrawingData.Hasher.NotSupplied, redrawScope);
		}

		public static GraphGizmoHelper GetGizmoHelper (DrawingData gizmos, AstarPath active, DrawingData.Hasher hasher, RedrawScope redrawScope) {
			var helper = ObjectPool<GraphGizmoHelper>.Claim();

			helper.Init(active, hasher, gizmos, redrawScope);
			return helper;
		}

		public void Init (AstarPath active, DrawingData.Hasher hasher, DrawingData gizmos, RedrawScope redrawScope) {
			if (active != null) {
				debugData = active.debugPathData;
				debugPathID = active.debugPathID;
				debugMode = active.debugMode;
				debugFloor = active.debugFloor;
				debugRoof = active.debugRoof;
				showSearchTree = active.showSearchTree && debugData != null;
			}
			this.hasher = hasher;
			builder = gizmos.GetBuilder(hasher, redrawScope);
		}

		public void OnEnterPool () {
			builder.Dispose();
			debugData = null;
		}

		public void DrawConnections (GraphNode node) {
			if (showSearchTree) {
				if (InSearchTree(node, debugData, debugPathID)) {
					var pnode = debugData.GetPathNode(node);
					if (pnode.parent != null) {
						builder.Line((Vector3)node.position, (Vector3)debugData.GetPathNode(node).parent.node.position, NodeColor(node));
					}
				}
			} else {
				// Calculate which color to use for drawing the node
				// based on the settings specified in the editor
				drawConnectionColor = NodeColor(node);
				// Get the node position
				// Cast it here to avoid doing it for every neighbour
				drawConnectionStart = (Vector3)node.position;
				node.GetConnections(drawConnection);
			}
		}

		void DrawConnection (GraphNode other) {
			builder.Line(drawConnectionStart, ((Vector3)other.position + drawConnectionStart)*0.5f, drawConnectionColor);
		}

		/// <summary>
		/// Color to use for gizmos.
		/// Returns a color to be used for the specified node with the current debug settings (editor only).
		///
		/// Version: Since 3.6.1 this method will not handle null nodes
		/// </summary>
		public Color NodeColor (GraphNode node) {
			if (showSearchTree && !InSearchTree(node, debugData, debugPathID)) return Color.clear;

			Color color;

			if (node.Walkable) {
				switch (debugMode) {
				case GraphDebugMode.Areas:
					color = AstarColor.GetAreaColor(node.Area);
					break;
				case GraphDebugMode.HierarchicalNode:
					color = AstarColor.GetTagColor((uint)node.HierarchicalNodeIndex);
					break;
				case GraphDebugMode.Penalty:
					color = Color.Lerp(AstarColor.ConnectionLowLerp, AstarColor.ConnectionHighLerp, ((float)node.Penalty-debugFloor) / (debugRoof-debugFloor));
					break;
				case GraphDebugMode.Tags:
					color = AstarColor.GetTagColor(node.Tag);
					break;
				case GraphDebugMode.SolidColor:
					color = AstarColor.SolidColor;
					break;
				default:
					if (debugData == null) {
						color = AstarColor.SolidColor;
						break;
					}

					PathNode pathNode = debugData.GetPathNode(node);
					float value;
					if (debugMode == GraphDebugMode.G) {
						value = pathNode.G;
					} else if (debugMode == GraphDebugMode.H) {
						value = pathNode.H;
					} else {
						// mode == F
						value = pathNode.F;
					}

					color = Color.Lerp(AstarColor.ConnectionLowLerp, AstarColor.ConnectionHighLerp, (value-debugFloor) / (debugRoof-debugFloor));
					break;
				}
			} else {
				color = AstarColor.UnwalkableNode;
			}

			return color;
		}

		/// <summary>
		/// Returns if the node is in the search tree of the path.
		/// Only guaranteed to be correct if path is the latest path calculated.
		/// Use for gizmo drawing only.
		/// </summary>
		public static bool InSearchTree (GraphNode node, PathHandler handler, ushort pathID) {
			return handler.GetPathNode(node).pathID == pathID;
		}

		public void DrawWireTriangle (Vector3 a, Vector3 b, Vector3 c, Color color) {
			builder.Line(a, b, color);
			builder.Line(b, c, color);
			builder.Line(c, a, color);
		}

		public void DrawTriangles (Vector3[] vertices, Color[] colors, int numTriangles) {
			var triangles = ArrayPool<int>.Claim(numTriangles*3);

			for (int i = 0; i < numTriangles*3; i++) triangles[i] = i;
			builder.SolidMesh(vertices, triangles, colors, numTriangles*3, numTriangles*3);
			ArrayPool<int>.Release(ref triangles);
		}

		public void DrawWireTriangles (Vector3[] vertices, Color[] colors, int numTriangles) {
			for (int i = 0; i < numTriangles; i++) {
				DrawWireTriangle(vertices[i*3+0], vertices[i*3+1], vertices[i*3+2], colors[i*3+0]);
			}
		}

		void System.IDisposable.Dispose () {
			var tmp = this;

			ObjectPool<GraphGizmoHelper>.Release(ref tmp);
		}
	}
}
