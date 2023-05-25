using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Pathfinding {
	using Pathfinding.Util;
	using Pathfinding.Drawing;

	/// <summary>
	/// Connects two nodes with a direct connection.
	/// It is not possible to detect this link when following a path (which may be good or bad), for that you can use NodeLink2.
	///
	/// [Open online documentation to see images]
	///
	/// See: editing-graphs (view in online documentation for working links)
	/// </summary>
	[AddComponentMenu("Pathfinding/Link")]
	[HelpURL("http://arongranberg.com/astar/documentation/beta/class_pathfinding_1_1_node_link.php")]
	public class NodeLink : GraphModifier {
		/// <summary>End position of the link</summary>
		public Transform end;

		/// <summary>
		/// The connection will be this times harder/slower to traverse.
		/// Note that values lower than one will not always make the pathfinder choose this path instead of another path even though this one should
		/// lead to a lower total cost unless you also adjust the Heuristic Scale in A* Inspector -> Settings -> Pathfinding or disable the heuristic altogether.
		/// </summary>
		public float costFactor = 1.0f;

		/// <summary>Make a one-way connection</summary>
		public bool oneWay = false;

		/// <summary>Delete existing connection instead of adding one</summary>
		public bool deleteConnection = false;

		public Transform Start {
			get { return transform; }
		}

		public Transform End {
			get { return end; }
		}

		public override void OnGraphsPostUpdateBeforeAreaRecalculation () {
			Apply();
		}

		public static void DrawArch (Vector3 a, Vector3 b, Color color) {
			Vector3 dir = b - a;

			if (dir == Vector3.zero) return;

			var normal = Vector3.Cross(new Vector3(0, 1, 0), dir);
			var normalUp = Vector3.Cross(dir, normal).normalized;

			normalUp *= dir.magnitude*0.1f;

			var p1c = a + normalUp;
			var p2c = b + normalUp;

			var prev = a;
			using (Draw.WithColor(color)) {
				for (int i = 1; i <= 20; i++) {
					float t = i/20.0f;
					Vector3 p = CommandBuilder.EvaluateCubicBezier(a, p1c, p2c, b, t);
					Draw.Line(prev, p);
					prev = p;
				}
			}
		}

		/// <summary>
		/// Connects the start and end points using a link or refreshes the existing link.
		///
		/// If you have moved the link or otherwise modified it you need to call this method.
		///
		/// Warning: This must only be done when it is safe to update the graph structure.
		/// The easiest is to do it inside a work item. See <see cref="AstarPath.active.AddWorkItem"/>.
		/// </summary>
		public virtual void Apply () {
			if (Start == null || End == null || AstarPath.active == null) return;

			GraphNode startNode = AstarPath.active.GetNearest(Start.position).node;
			GraphNode endNode = AstarPath.active.GetNearest(End.position).node;

			if (startNode == null || endNode == null) return;


			if (deleteConnection) {
				startNode.RemoveConnection(endNode);
				if (!oneWay)
					endNode.RemoveConnection(startNode);
			} else {
				uint cost = (uint)System.Math.Round((startNode.position-endNode.position).costMagnitude*costFactor);

				startNode.AddConnection(endNode, cost);
				if (!oneWay)
					endNode.AddConnection(startNode, cost);
			}
		}

		public override void DrawGizmos () {
			if (Start == null || End == null) return;

			NodeLink.DrawArch(Start.position, End.position, deleteConnection ? Color.red : Color.green);
		}

#if UNITY_EDITOR
		[UnityEditor.MenuItem("Edit/Pathfinding/Link Pair %&l")]
		public static void LinkObjects () {
			Transform[] tfs = Selection.transforms;
			if (tfs.Length == 2) {
				LinkObjects(tfs[0], tfs[1], false);
			}
			SceneView.RepaintAll();
		}

		[UnityEditor.MenuItem("Edit/Pathfinding/Unlink Pair %&u")]
		public static void UnlinkObjects () {
			Transform[] tfs = Selection.transforms;
			if (tfs.Length == 2) {
				LinkObjects(tfs[0], tfs[1], true);
			}
			SceneView.RepaintAll();
		}

		[UnityEditor.MenuItem("Edit/Pathfinding/Delete Links on Selected %&b")]
		public static void DeleteLinks () {
			Transform[] tfs = Selection.transforms;
			for (int i = 0; i < tfs.Length; i++) {
				NodeLink[] conns = tfs[i].GetComponents<NodeLink>();
				for (int j = 0; j < conns.Length; j++) DestroyImmediate(conns[j]);
			}
			SceneView.RepaintAll();
		}

		public static void LinkObjects (Transform a, Transform b, bool removeConnection) {
			NodeLink connecting = null;

			NodeLink[] conns = a.GetComponents<NodeLink>();
			for (int i = 0; i < conns.Length; i++) {
				if (conns[i].end == b) {
					connecting = conns[i];
					break;
				}
			}

			conns = b.GetComponents<NodeLink>();
			for (int i = 0; i < conns.Length; i++) {
				if (conns[i].end == a) {
					connecting = conns[i];
					break;
				}
			}

			if (removeConnection) {
				if (connecting != null) DestroyImmediate(connecting);
			} else {
				if (connecting == null) {
					connecting = a.gameObject.AddComponent<NodeLink>();
					connecting.end = b;
				} else {
					connecting.deleteConnection = !connecting.deleteConnection;
				}
			}
		}
#endif
	}
}
