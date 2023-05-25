using UnityEngine;
using System.Collections.Generic;

namespace Pathfinding {
	/// <summary>
	/// Explicit mesh object for recast graphs.
	///
	/// Sometimes you want to tweak the navmesh on a per-object basis. For example you might want to make some objects completely unwalkable, or you might want to special case some objects to remove them from the navmesh altogether.
	///
	/// You can do this using the <see cref="RecastMeshObj"/> component. Attach it to any object you want to modify and configure the settings as you wish.
	///
	/// [Open online documentation to see images]
	///
	/// Using the <see cref="RecastMeshObj"/> component you can:
	///
	/// - Exclude an object from the graph completely.
	/// - Make the surfaces of an object unwalkable.
	/// - Make the surfaces of an object walkable (this is just the default behavior).
	/// - Create seams in the navmesh between adjacent objects.
	/// - Mark the surfaces of an object with a specific tag (see tags) (view in online documentation for working links).
	///
	/// Adding this component to an object will make sure it is included in any recast graphs.
	/// It will be included even if the Rasterize Meshes toggle is set to false.
	///
	/// Using RecastMeshObjs instead of relying on the Rasterize Meshes option is good for several reasons.
	/// - Rasterize Meshes is slow. If you are using a tiled graph and you are updating it, every time something is recalculated
	/// the graph will have to search all meshes in your scene for ones to rasterize, in contrast, RecastMeshObjs are stored
	/// in a tree for extremely fast lookup (O(log n + k) compared to O(n) where n is the number of meshes in your scene and k is the number of meshes
	/// which should be rasterized, if you know Big-O notation).
	/// - The RecastMeshObj exposes some options which can not be accessed using the Rasterize Meshes toggle. See member documentation for more info.
	///      This can for example be used to include meshes in the recast graph rasterization, but make sure that the character cannot walk on them.
	///
	/// Since the objects are stored in a tree, and trees are slow to update, there is an enforcement that objects are not allowed to move
	/// unless the <see cref="dynamic"/> option is enabled. When the dynamic option is enabled, the object will be stored in an array instead of in the tree.
	/// This will reduce the performance improvement over 'Rasterize Meshes' but is still faster.
	///
	/// If a mesh filter and a mesh renderer is attached to this GameObject, those will be used in the rasterization
	/// otherwise if a collider is attached, that will be used.
	/// </summary>
	[AddComponentMenu("Pathfinding/Navmesh/RecastMeshObj")]
	[HelpURL("http://arongranberg.com/astar/documentation/beta/class_pathfinding_1_1_recast_mesh_obj.php")]
	public class RecastMeshObj : VersionedMonoBehaviour {
		/// <summary>Static objects are stored in a tree for fast bounds lookups</summary>
		protected static RecastBBTree tree = new RecastBBTree();

		/// <summary>Dynamic objects are stored in a list since it is costly to update the tree every time they move</summary>
		protected static List<RecastMeshObj> dynamicMeshObjs = new List<RecastMeshObj>();

		/// <summary>Fills the buffer with all RecastMeshObjs which intersect the specified bounds</summary>
		public static void GetAllInBounds (List<RecastMeshObj> buffer, Bounds bounds) {
			if (!Application.isPlaying) {
				var objs = FindObjectsOfType(typeof(RecastMeshObj)) as RecastMeshObj[];
				for (int i = 0; i < objs.Length; i++) {
					if (objs[i].enabled) {
						objs[i].RecalculateBounds();
						if (objs[i].GetBounds().Intersects(bounds)) {
							buffer.Add(objs[i]);
						}
					}
				}
				return;
			} else if (Time.timeSinceLevelLoad == 0) {
				// Is is not guaranteed that all RecastMeshObj OnEnable functions have been called, so if it is the first frame since loading a new level
				// try to initialize all RecastMeshObj objects.
				var objs = FindObjectsOfType(typeof(RecastMeshObj)) as RecastMeshObj[];
				for (int i = 0; i < objs.Length; i++) objs[i].Register();
			}

			for (int q = 0; q < dynamicMeshObjs.Count; q++) {
				if (dynamicMeshObjs[q].GetBounds().Intersects(bounds)) {
					buffer.Add(dynamicMeshObjs[q]);
				}
			}

			Rect r = Rect.MinMaxRect(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z);

			tree.QueryInBounds(r, buffer);
		}

		[HideInInspector]
		public Bounds bounds;

		/// <summary>
		/// Check if the object will move.
		/// Recalculation of bounding box trees is expensive so if this is true, the object
		/// will simply be stored in an array. Easier to move, but slower lookup, so use wisely.
		/// If you for some reason want to move it, but don't want it dynamic (maybe you will only move it veery seldom and have lots of similar
		/// objects so they would add overhead by being dynamic). You can enable and disable the component every time you move it.
		/// Disabling it will remove it from the bounding box tree and enabling it will add it to the bounding box tree again.
		///
		/// The object should never move unless being dynamic or disabling/enabling it as described above.
		/// </summary>
		public bool dynamic = true;

		/// <summary>
		/// If true then the mesh will be treated as solid and its interior will be unwalkable.
		/// The unwalkable region will be the minimum to maximum y coordinate in each cell.
		///
		/// If you enable this on a mesh that is actually hollow then the hollow region will also be treated as unwalkable.
		/// </summary>
		public bool solid = false;

		public enum Mode {
			/// <summary>This object will be completely ignored by the graph</summary>
			ExcludeFromGraph,
			/// <summary>All surfaces on this mesh will be made unwalkable</summary>
			UnwalkableSurface,
			/// <summary>All surfaces on this mesh will be walkable</summary>
			WalkableSurface,
			/// <summary>All surfaces on this mesh will be walkable and a seam will be created between the surfaces on this mesh and the surfaces on other meshes (with a different surface id)</summary>
			WalkableSurfaceWithSeam,
			/// <summary>All surfaces on this mesh will be walkable and the nodes will be given the specified tag. A seam will be created between the surfaces on this mesh and the surfaces on other meshes (with a different tag or surface id)</summary>
			WalkableSurfaceWithTag,
		}

		/// <summary>
		/// Voxel area for mesh.
		/// This area (not to be confused with pathfinding areas, this is only used when rasterizing meshes for the recast graph) field
		/// can be used to explicitly insert edges in the navmesh geometry or to make some parts of the mesh unwalkable.
		///
		/// When rasterizing the world and two objects with different surface id values are adjacent to each other, a split in the navmesh geometry
		/// will be added between them, characters will still be able to walk between them, but this can be useful when working with navmesh updates.
		///
		/// Navmesh updates which recalculate a whole tile (updatePhysics=True) are very slow So if there are special places
		/// which you know are going to be updated quite often, for example at a door opening (opened/closed door) you
		/// can use surface IDs to create splits on the navmesh for easier updating using normal graph updates (updatePhysics=False).
		/// See the below video for more information.
		///
		/// Video: https://www.youtube.com/watch?v=CS6UypuEMwM
		///
		/// Deprecated: Use <see cref="mode"/> and <see cref="surfaceID"/> instead
		/// </summary>
		[System.Obsolete("Use mode and surfaceID instead")]
		public int area {
			get {
				switch (mode) {
				case Mode.ExcludeFromGraph:
				case Mode.UnwalkableSurface:
					return -1;
				case Mode.WalkableSurface:
				default:
					return 0;
				case Mode.WalkableSurfaceWithSeam:
					return surfaceID;
				case Mode.WalkableSurfaceWithTag:
					return surfaceID;
				}
			}
			set {
				if (value <= -1) mode = Mode.UnwalkableSurface;
				if (value == 0) mode = Mode.WalkableSurface;
				if (value > 0) {
					mode = Mode.WalkableSurfaceWithSeam;
					surfaceID = value;
				}
			}
		}

		/// <summary>
		/// Voxel area for mesh.
		/// This area (not to be confused with pathfinding areas, this is only used when rasterizing meshes for the recast graph) field
		/// can be used to explicitly insert edges in the navmesh geometry or to make some parts of the mesh unwalkable.
		///
		/// When rasterizing the world and two objects with different surface id values are adjacent to each other, a split in the navmesh geometry
		/// will be added between them, characters will still be able to walk between them, but this can be useful when working with navmesh updates.
		///
		/// Navmesh updates which recalculate a whole tile (updatePhysics=True) are very slow So if there are special places
		/// which you know are going to be updated quite often, for example at a door opening (opened/closed door) you
		/// can use surface IDs to create splits on the navmesh for easier updating using normal graph updates (updatePhysics=False).
		/// See the below video for more information.
		///
		/// Video: https://www.youtube.com/watch?v=CS6UypuEMwM
		///
		/// When <see cref="mode"/> is set to Mode.WalkableSurfaceWithTag then this value will be interpreted as a pathfinding tag. See tags (view in online documentation for working links).
		///
		/// Note: This only has an effect if <see cref="mode"/> is set to Mode.WalkableSurfaceWithSeam or Mode.WalkableSurfaceWithTag.
		///
		/// Note: Only non-negative values are valid.
		/// </summary>
		[UnityEngine.Serialization.FormerlySerializedAs("area")]
		public int surfaceID = 1;

		/// <summary>
		/// Surface rasterization mode.
		/// See: <see cref="Mode"/>
		/// </summary>
		public Mode mode = Mode.WalkableSurface;

		bool _dynamic;
		bool registered;

		void OnEnable () {
			Register();
		}

		void Register () {
			if (registered) return;

			registered = true;

			// Clamp area, upper limit isn't really a hard limit, but if it gets much higher it will start to interfere with other stuff
			surfaceID = Mathf.Clamp(surfaceID, 0, 1 << 25);

			Renderer rend = GetComponent<Renderer>();

			Collider coll = GetComponent<Collider>();
			if (rend == null && coll == null) throw new System.Exception("A renderer or a collider should be attached to the GameObject");

			MeshFilter filter = GetComponent<MeshFilter>();

			if (rend != null && filter == null) throw new System.Exception("A renderer was attached but no mesh filter");

			// Default to renderer
			bounds = rend != null ? rend.bounds : coll.bounds;

			_dynamic = dynamic;
			if (_dynamic) {
				dynamicMeshObjs.Add(this);
			} else {
				tree.Insert(this);
			}
		}

		/// <summary>Recalculates the internally stored bounds of the object</summary>
		private void RecalculateBounds () {
			Renderer rend = GetComponent<Renderer>();

			Collider coll = GetCollider();

			if (rend == null && coll == null) throw new System.Exception("A renderer or a collider should be attached to the GameObject");

			MeshFilter filter = GetComponent<MeshFilter>();

			if (rend != null && filter == null) throw new System.Exception("A renderer was attached but no mesh filter");

			// Default to renderer
			bounds = rend != null ? rend.bounds : coll.bounds;
		}

		/// <summary>Bounds completely enclosing the mesh for this object</summary>
		public Bounds GetBounds () {
			if (_dynamic) {
				RecalculateBounds();
			}
			return bounds;
		}

		public MeshFilter GetMeshFilter () {
			return GetComponent<MeshFilter>();
		}

		public Collider GetCollider () {
			return GetComponent<Collider>();
		}

		void OnDisable () {
			registered = false;

			if (_dynamic) {
				dynamicMeshObjs.Remove(this);
			} else {
				if (!tree.Remove(this)) {
					throw new System.Exception("Could not remove RecastMeshObj from tree even though it should exist in it. Has the object moved without being marked as dynamic?");
				}
			}
			_dynamic = dynamic;
		}

		protected override int OnUpgradeSerializedData (int version, bool unityThread) {
			#pragma warning disable 618
			if (version == 1) area = surfaceID;
			#pragma warning restore 618
			return 2;
		}
	}
}
