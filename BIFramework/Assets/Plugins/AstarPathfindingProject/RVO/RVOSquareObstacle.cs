using UnityEngine;

namespace Pathfinding.RVO {
	/// <summary>
	/// Square Obstacle for RVO Simulation.
	///
	/// Deprecated: This component is deprecated. Local avoidance colliders never worked particularly well and the performance was poor. Modify the graphs instead so that pathfinding takes obstacles into account.
	/// </summary>
	[AddComponentMenu("")]
	[HelpURL("http://arongranberg.com/astar/documentation/beta/class_pathfinding_1_1_r_v_o_1_1_r_v_o_square_obstacle.php")]
	public class RVOSquareObstacle : RVOObstacle {
		/// <summary>Height of the obstacle</summary>
		public float height = 1;

		/// <summary>Size of the square</summary>
		public Vector2 size = Vector3.one;

		/// <summary>Center of the square</summary>
		public Vector2 center = Vector3.zero;

		protected override bool StaticObstacle { get { return false; } }
		protected override bool ExecuteInEditor { get { return true; } }
		protected override bool LocalCoordinates { get { return true; } }
		protected override float Height { get { return height; } }
	}
}
