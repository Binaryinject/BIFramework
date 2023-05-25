using Math = System.Math;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Profiling;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;

namespace Pathfinding {
	using Pathfinding.Voxels;
	using Pathfinding.Voxels.Burst;
	using Pathfinding.Serialization;
	using Pathfinding.Recast;
	using Pathfinding.Util;
	using Pathfinding.Jobs;

	/// <summary>
	/// Automatically generates navmesh graphs based on world geometry.
	///
	/// [Open online documentation to see images]
	///
	/// The recast graph is based on Recast (http://code.google.com/p/recastnavigation/).
	/// I have translated a good portion of it to C# to run it natively in Unity.
	///
	/// For a tutorial on how to configure a recast graph, take a look at create-recast (view in online documentation for working links).
	///
	/// [Open online documentation to see images]
	///
	/// \section howitworks How a recast graph works
	/// When generating a recast graph what happens is that the world is voxelized.
	/// You can think of this as constructing an approximation of the world out of lots of boxes.
	/// If you have played Minecraft it looks very similar (but with smaller boxes).
	/// [Open online documentation to see images]
	///
	/// The Recast process is described as follows:
	/// - The voxel mold is build from the input triangle mesh by rasterizing the triangles into a multi-layer heightfield.
	/// Some simple filters are then applied to the mold to prune out locations where the character would not be able to move.
	/// - The walkable areas described by the mold are divided into simple overlayed 2D regions.
	/// The resulting regions have only one non-overlapping contour, which simplifies the final step of the process tremendously.
	/// - The navigation polygons are peeled off from the regions by first tracing the boundaries and then simplifying them.
	/// The resulting polygons are finally converted to triangles which makes them perfect for pathfinding and spatial reasoning about the level.
	///
	/// The recast generation process usually works directly on the visiable geometry in the world. This is usually a good thing, because world geometry is usually more detailed than the colliders.
	/// You can, however, specify that colliders should be rasterized instead. If you have very detailed world geometry, this can speed up scanning and updating the graph.
	///
	/// \section export Exporting for manual editing
	/// In the editor there is a button for exporting the generated graph to a .obj file.
	/// Usually the generation process is good enough for the game directly, but in some cases you might want to edit some minor details.
	/// So you can export the graph to a .obj file, open it in your favourite 3D application, edit it, and export it to a mesh which Unity can import.
	/// You can then use that mesh in a navmesh graph.
	///
	/// Since many 3D modelling programs use different axis systems (unity uses X=right, Y=up, Z=forward), it can be a bit tricky to get the rotation and scaling right.
	/// For blender for example, what you have to do is to first import the mesh using the .obj importer. Don't change anything related to axes in the settings.
	/// Then select the mesh, open the transform tab (usually the thin toolbar to the right of the 3D view) and set Scale -> Z to -1.
	/// If you transform it using the S (scale) hotkey, it seems to set both Z and Y to -1 for some reason.
	/// Then make the edits you need and export it as an .obj file to somewhere in the Unity project.
	/// But this time, edit the setting named "Forward" to "Z forward" (not -Z as it is per default).
	/// </summary>
	[JsonOptIn]
	[Pathfinding.Util.Preserve]
	public class RecastGraph : NavmeshBase, IUpdatableGraph {
		[JsonMember]
		/// <summary>
		/// Radius of the agent which will traverse the navmesh.
		/// The navmesh will be eroded with this radius.
		/// [Open online documentation to see images]
		/// </summary>
		public float characterRadius = 1.5F;

		/// <summary>
		/// Max distance from simplified edge to real edge.
		/// This value is measured in voxels. So with the default value of 2 it means that the final navmesh contour may be at most
		/// 2 voxels (i.e 2 times <see cref="cellSize)"/> away from the border that was calculated when voxelizing the world.
		/// A higher value will yield a more simplified and cleaner navmesh while a lower value may capture more details.
		/// However a too low value will cause the individual voxels to be visible (see image below).
		///
		/// [Open online documentation to see images]
		///
		/// See: <see cref="cellSize"/>
		/// </summary>
		[JsonMember]
		public float contourMaxError = 2F;

		/// <summary>
		/// Voxel sample size (x,z).
		/// When generating a recast graph what happens is that the world is voxelized.
		/// You can think of this as constructing an approximation of the world out of lots of boxes.
		/// If you have played Minecraft it looks very similar (but with smaller boxes).
		/// [Open online documentation to see images]
		/// The cell size is the width and depth of those boxes. The height of the boxes is usually much smaller
		/// and automatically calculated however. See <see cref="CellHeight"/>.
		///
		/// Lower values will yield higher quality navmeshes, however the graph will be slower to scan.
		///
		/// [Open online documentation to see images]
		/// </summary>
		[JsonMember]
		public float cellSize = 0.5F;

		/// <summary>
		/// Character height.
		/// [Open online documentation to see images]
		/// </summary>
		[JsonMember]
		public float walkableHeight = 2F;

		/// <summary>
		/// Height the character can climb.
		/// [Open online documentation to see images]
		/// </summary>
		[JsonMember]
		public float walkableClimb = 0.5F;

		/// <summary>
		/// Max slope in degrees the character can traverse.
		/// [Open online documentation to see images]
		/// </summary>
		[JsonMember]
		public float maxSlope = 30;

		/// <summary>
		/// Longer edges will be subdivided.
		/// Reducing this value can sometimes improve path quality since similarly sized triangles
		/// yield better paths than really large and really triangles small next to each other.
		/// However it will also add a lot more nodes which will make pathfinding slower.
		/// For more information about this take a look at navmeshnotes (view in online documentation for working links).
		///
		/// [Open online documentation to see images]
		/// </summary>
		[JsonMember]
		public float maxEdgeLength = 20;

		/// <summary>
		/// Minumum region size.
		/// Small regions will be removed from the navmesh.
		/// Measured in square world units (square meters in most games).
		///
		/// [Open online documentation to see images]
		///
		/// If a region is adjacent to a tile border, it will not be removed
		/// even though it is small since the adjacent tile might join it
		/// to form a larger region.
		///
		/// [Open online documentation to see images]
		/// [Open online documentation to see images]
		/// </summary>
		[JsonMember]
		public float minRegionSize = 3;

		/// <summary>
		/// Size in voxels of a single tile.
		/// This is the width of the tile.
		///
		/// [Open online documentation to see images]
		///
		/// A large tile size can be faster to initially scan (but beware of out of memory issues if you try with a too large tile size in a large world)
		/// smaller tile sizes are (much) faster to update.
		///
		/// Different tile sizes can affect the quality of paths. It is often good to split up huge open areas into several tiles for
		/// better quality paths, but too small tiles can also lead to effects looking like invisible obstacles.
		/// For more information about this take a look at navmeshnotes (view in online documentation for working links).
		/// Usually it is best to experiment and see what works best for your game.
		///
		/// When scanning a recast graphs individual tiles can be calculated in parallel which can make it much faster to scan large worlds.
		/// When you want to recalculate a part of a recast graph, this can only be done on a tile-by-tile basis which means that if you often try to update a region
		/// of the recast graph much smaller than the tile size, then you will be doing a lot of unnecessary calculations. However if you on the other hand
		/// update regions of the recast graph that are much larger than the tile size then it may be slower than necessary as there is some overhead in having lots of tiles
		/// instead of a few larger ones (not that much though).
		///
		/// Recommended values are between 64 and 256, but these are very soft limits. It is possible to use both larger and smaller values.
		/// </summary>
		[JsonMember]
		public int editorTileSize = 128;

		/// <summary>
		/// Size of a tile along the X axis in voxels.
		/// \copydetails editorTileSize
		///
		/// Warning: Do not modify, it is set from <see cref="editorTileSize"/> at Scan
		///
		/// See: <see cref="tileSizeZ"/>
		/// </summary>
		[JsonMember]
		public int tileSizeX = 128;

		/// <summary>
		/// Size of a tile along the Z axis in voxels.
		/// \copydetails editorTileSize
		///
		/// Warning: Do not modify, it is set from <see cref="editorTileSize"/> at Scan
		///
		/// See: <see cref="tileSizeX"/>
		/// </summary>
		[JsonMember]
		public int tileSizeZ = 128;


		/// <summary>
		/// If true, divide the graph into tiles, otherwise use a single tile covering the whole graph.
		///
		/// Using tiles is useful for a number of things. But it also has some drawbacks.
		/// - Using tiles allows you to update only a part of the graph at a time. When doing graph updates on a recast graph, it will always recalculate whole tiles (or the whole graph if there are no tiles).
		///    <see cref="NavmeshCut"/> components also work on a tile-by-tile basis.
		/// - Using tiles allows you to use <see cref="NavmeshPrefab"/>s.
		/// - Using tiles can break up very large triangles, which can improve path quality in some cases, and make the navmesh more closely follow the y-coordinates of the ground.
		/// - Using tiles can make it much faster to generate the navmesh, because each tile can be calculated in parallel.
		///    But if the tiles are made too small, then the overhead of having many tiles can make it slower than having fewer tiles.
		/// - Using small tiles can make the path quality worse in some cases, but setting the <see cref="FunnelModifier"/>s quality setting to high (or using <see cref="RichAI.funnelSimplification"/>) will mostly mitigate this.
		///
		/// See: <see cref="editorTileSize"/>
		///
		/// Since: Since 4.1 the default value is true.
		/// </summary>
		[JsonMember]
		public bool useTiles = true;

		/// <summary>
		/// If true, scanning the graph will yield a completely empty graph.
		/// Useful if you want to replace the graph with a custom navmesh for example
		/// </summary>
		public bool scanEmptyGraph;

		public enum RelevantGraphSurfaceMode {
			/// <summary>No RelevantGraphSurface components are required anywhere</summary>
			DoNotRequire,
			/// <summary>
			/// Any surfaces that are completely inside tiles need to have a <see cref="Pathfinding.RelevantGraphSurface"/> component
			/// positioned on that surface, otherwise it will be stripped away.
			/// </summary>
			OnlyForCompletelyInsideTile,
			/// <summary>
			/// All surfaces need to have one <see cref="Pathfinding.RelevantGraphSurface"/> component
			/// positioned somewhere on the surface and in each tile that it touches, otherwise it will be stripped away.
			/// Only tiles that have a RelevantGraphSurface component for that surface will keep it.
			/// </summary>
			RequireForAll
		}

		/// <summary>
		/// Require every region to have a RelevantGraphSurface component inside it.
		/// A RelevantGraphSurface component placed in the scene specifies that
		/// the navmesh region it is inside should be included in the navmesh.
		///
		/// If this is set to OnlyForCompletelyInsideTile
		/// a navmesh region is included in the navmesh if it
		/// has a RelevantGraphSurface inside it, or if it
		/// is adjacent to a tile border. This can leave some small regions
		/// which you didn't want to have included because they are adjacent
		/// to tile borders, but it removes the need to place a component
		/// in every single tile, which can be tedious (see below).
		///
		/// If this is set to RequireForAll
		/// a navmesh region is included only if it has a RelevantGraphSurface
		/// inside it. Note that even though the navmesh
		/// looks continous between tiles, the tiles are computed individually
		/// and therefore you need a RelevantGraphSurface component for each
		/// region and for each tile.
		///
		/// [Open online documentation to see images]
		/// In the above image, the mode OnlyForCompletelyInsideTile was used. Tile borders
		/// are highlighted in black. Note that since all regions are adjacent to a tile border,
		/// this mode didn't remove anything in this case and would give the same result as DoNotRequire.
		/// The RelevantGraphSurface component is shown using the green gizmo in the top-right of the blue plane.
		///
		/// [Open online documentation to see images]
		/// In the above image, the mode RequireForAll was used. No tiles were used.
		/// Note that the small region at the top of the orange cube is now gone, since it was not the in the same
		/// region as the relevant graph surface component.
		/// The result would have been identical with OnlyForCompletelyInsideTile since there are no tiles (or a single tile, depending on how you look at it).
		///
		/// [Open online documentation to see images]
		/// The mode RequireForAll was used here. Since there is only a single RelevantGraphSurface component, only the region
		/// it was in, in the tile it is placed in, will be enabled. If there would have been several RelevantGraphSurface in other tiles,
		/// those regions could have been enabled as well.
		///
		/// [Open online documentation to see images]
		/// Here another tile size was used along with the OnlyForCompletelyInsideTile.
		/// Note that the region on top of the orange cube is gone now since the region borders do not intersect that region (and there is no
		/// RelevantGraphSurface component inside it).
		///
		/// Note: When not using tiles. OnlyForCompletelyInsideTile is equivalent to RequireForAll.
		/// </summary>
		[JsonMember]
		public RelevantGraphSurfaceMode relevantGraphSurfaceMode = RelevantGraphSurfaceMode.DoNotRequire;

		[JsonMember]
		/// <summary>Use colliders to calculate the navmesh</summary>
		public bool rasterizeColliders;

		[JsonMember]
		/// <summary>Use scene meshes to calculate the navmesh</summary>
		public bool rasterizeMeshes = true;

		/// <summary>Include the Terrain in the scene.</summary>
		[JsonMember]
		public bool rasterizeTerrain = true;

		/// <summary>
		/// Rasterize tree colliders on terrains.
		///
		/// If the tree prefab has a collider, that collider will be rasterized.
		/// Otherwise a simple box collider will be used and the script will
		/// try to adjust it to the tree's scale, it might not do a very good job though so
		/// an attached collider is preferable.
		///
		/// Note: It seems that Unity will only generate tree colliders at runtime when the game is started.
		/// For this reason, this graph will not pick up tree colliders when scanned outside of play mode
		/// but it will pick them up if the graph is scanned when the game has started. If it still does not pick them up
		/// make sure that the trees actually have colliders attached to them and that the tree prefabs are
		/// in the correct layer (the layer should be included in the layer mask).
		///
		/// See: rasterizeTerrain
		/// See: colliderRasterizeDetail
		/// </summary>
		[JsonMember]
		public bool rasterizeTrees = true;

		/// <summary>
		/// Controls detail on rasterization of sphere and capsule colliders.
		/// This controls the number of rows and columns on the generated meshes.
		/// A higher value does not necessarily increase quality of the mesh, but a lower
		/// value will often speed it up.
		///
		/// You should try to keep this value as low as possible without affecting the mesh quality since
		/// that will yield the fastest scan times.
		///
		/// See: rasterizeColliders
		/// </summary>
		[JsonMember]
		public float colliderRasterizeDetail = 10;

		/// <summary>
		/// Layer mask which filters which objects to include.
		/// See: tagMask
		/// </summary>
		[JsonMember]
		public LayerMask mask = -1;

		/// <summary>
		/// Objects tagged with any of these tags will be rasterized.
		/// Note that this extends the layer mask, so if you only want to use tags, set <see cref="mask"/> to 'Nothing'.
		///
		/// See: mask
		/// </summary>
		[JsonMember]
		public List<string> tagMask = new List<string>();

		/// <summary>
		/// Controls how large the sample size for the terrain is.
		/// A higher value is faster to scan but less accurate
		/// </summary>
		[JsonMember]
		public int terrainSampleSize = 3;

		/// <summary>Rotation of the graph in degrees</summary>
		[JsonMember]
		public Vector3 rotation;

		/// <summary>
		/// Center of the bounding box.
		/// Scanning will only be done inside the bounding box
		/// </summary>
		[JsonMember]
		public Vector3 forcedBoundsCenter;

#if !UNITY_2020_1_OR_NEWER
		private Voxelize globalVox;
#endif

#if UNITY_EDITOR
		/// <summary>Internal field used to warn users when the mesh includes meshes that are not readable at runtime</summary>
		public List<Mesh> meshesUnreadableAtRuntime;
#endif

		public const int BorderVertexMask = 1;
		public const int BorderVertexOffset = 31;

		/// <summary>
		/// List of tiles that have been calculated in a graph update, but have not yet been added to the graph.
		/// When updating the graph in a separate thread, large changes cannot be made directly to the graph
		/// as other scripts might use the graph data structures at the same time in another thread.
		/// So the tiles are calculated, but they are not yet connected to the existing tiles
		/// that will be done in UpdateAreaPost which runs in the Unity thread.
		///
		/// Note: Should not contain duplicates.
		/// </summary>
		List<NavmeshTile> stagingTiles = new List<NavmeshTile>();

		protected override bool RecalculateNormals { get { return true; } }

		public override float TileWorldSizeX {
			get {
				return tileSizeX*cellSize;
			}
		}

		public override float TileWorldSizeZ {
			get {
				return tileSizeZ*cellSize;
			}
		}

		protected override float MaxTileConnectionEdgeDistance {
			get {
				return walkableClimb;
			}
		}

		/// <summary>
		/// World bounds for the graph.
		/// Defined as a bounds object with size <see cref="forcedBoundsSize"/> and centered at <see cref="forcedBoundsCenter"/>
		/// Deprecated: Obsolete since this is not accurate when the graph is rotated (rotation was not supported when this property was created)
		/// </summary>
		[System.Obsolete("Obsolete since this is not accurate when the graph is rotated (rotation was not supported when this property was created)")]
		public Bounds forcedBounds {
			get {
				return new Bounds(forcedBoundsCenter, forcedBoundsSize);
			}
		}

		/// <summary>
		/// Returns the closest point of the node.
		/// Deprecated: Use <see cref="Pathfinding.TriangleMeshNode.ClosestPointOnNode"/> instead
		/// </summary>
		[System.Obsolete("Use node.ClosestPointOnNode instead")]
		public Vector3 ClosestPointOnNode (TriangleMeshNode node, Vector3 pos) {
			return node.ClosestPointOnNode(pos);
		}

		/// <summary>
		/// Returns if the point is inside the node in XZ space.
		/// Deprecated: Use <see cref="Pathfinding.TriangleMeshNode.ContainsPoint"/> instead
		/// </summary>
		[System.Obsolete("Use node.ContainsPoint instead")]
		public bool ContainsPoint (TriangleMeshNode node, Vector3 pos) {
			return node.ContainsPoint((Int3)pos);
		}

		/// <summary>
		/// Changes the bounds of the graph to precisely encapsulate all objects in the scene that can be included in the scanning process based on the settings.
		/// Which objects are used depends on the settings. If an object would have affected the graph with the current settings if it would have
		/// been inside the bounds of the graph, it will be detected and the bounds will be expanded to contain that object.
		///
		/// This method corresponds to the 'Snap bounds to scene' button in the inspector.
		///
		/// See: rasterizeMeshes
		/// See: rasterizeTerrain
		/// See: rasterizeColliders
		/// See: mask
		/// See: tagMask
		///
		/// See: forcedBoundsCenter
		/// See: forcedBoundsSize
		/// </summary>
		public void SnapForceBoundsToScene () {
			var meshes = CollectMeshes(new Bounds(Vector3.zero, new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity)));

			if (meshes.Count == 0) {
				return;
			}

			var bounds = meshes[0].bounds;

			for (int i = 1; i < meshes.Count; i++) {
				bounds.Encapsulate(meshes[i].bounds);
				meshes[i].Pool();
			}

			forcedBoundsCenter = bounds.center;
			forcedBoundsSize = bounds.size;
		}

		GraphUpdateThreading IUpdatableGraph.CanUpdateAsync (GraphUpdateObject o) {
#if UNITY_2020_1_OR_NEWER
			return o.updatePhysics ? GraphUpdateThreading.UnityInit | GraphUpdateThreading.UnityPost : GraphUpdateThreading.SeparateThread;
#else
			return o.updatePhysics ? GraphUpdateThreading.UnityInit | GraphUpdateThreading.SeparateThread | GraphUpdateThreading.UnityPost : GraphUpdateThreading.SeparateThread;
#endif
		}

#if UNITY_2020_1_OR_NEWER
		Promise<RecastBuilder.BuildTileMeshesOutput> pendingGraphUpdatePromise;
		Promise<RecastBuilder.BuildNodeTilesOutput> pendingGraphUpdatePromise2;
		DisposeArena pendingGraphUpdateArena = new DisposeArena();
#endif

		GraphUpdatePromise IUpdatableGraph.UpdateAreaInit (GraphUpdateObject o) {
			if (!o.updatePhysics) {
				return null;
			}

#if UNITY_2020_1_OR_NEWER
			// Calculate world bounds of all affected tiles
			// Expand TileBorderSizeInWorldUnits voxels in all directions to make sure
			// all tiles that could be affected by the update are recalculated.
			IntRect touchingTiles = GetTouchingTiles(o.bounds, TileBorderSizeInWorldUnits);
			if (touchingTiles.IsValid()) {
				this.pendingGraphUpdatePromise = RecastBuilder.BuildTileMeshes(this, touchingTiles).Schedule(pendingGraphUpdateArena);
				this.pendingGraphUpdatePromise2 = RecastBuilder.BuildNodeTiles(this).Schedule(pendingGraphUpdateArena, this.pendingGraphUpdatePromise);
			} else {
				this.pendingGraphUpdatePromise = default;
				this.pendingGraphUpdatePromise2 = default;
			}
			return this.pendingGraphUpdatePromise2;
#else
			AstarProfiler.Reset();
			AstarProfiler.StartProfile("UpdateAreaInit");
			AstarProfiler.StartProfile("CollectMeshes");

			RelevantGraphSurface.UpdateAllPositions();

			// Calculate world bounds of all affected tiles
			// Expand TileBorderSizeInWorldUnits voxels in all directions to make sure
			// all tiles that could be affected by the update are recalculated.
			IntRect touchingTiles = GetTouchingTiles(o.bounds, TileBorderSizeInWorldUnits);
			Bounds tileBounds = GetTileBounds(touchingTiles);

			// Expand TileBorderSizeInWorldUnits voxels in all directions to make sure we grab all meshes that could affect the tiles.
			tileBounds.Expand(new Vector3(1, 0, 1)*TileBorderSizeInWorldUnits*2);

			var meshes = CollectMeshes(tileBounds);

			if (globalVox == null) {
				// Create the voxelizer and set all settings
				globalVox = new Voxelize(CellHeight, cellSize, walkableClimb, walkableHeight, maxSlope, maxEdgeLength);
			}

			globalVox.inputMeshes = meshes;

			AstarProfiler.EndProfile("CollectMeshes");
			AstarProfiler.EndProfile("UpdateAreaInit");
			return null;
#endif
		}

		void IUpdatableGraph.UpdateArea (GraphUpdateObject guo) {
			// Figure out which tiles are affected
			// Expand TileBorderSizeInWorldUnits voxels in all directions to make sure
			// all tiles that could be affected by the update are recalculated.
			var affectedTiles = GetTouchingTiles(guo.bounds, TileBorderSizeInWorldUnits);

			// If the bounding box did not overlap with the graph then just skip the update
			if (!affectedTiles.IsValid()) return;

			if (!guo.updatePhysics) {
				for (int z = affectedTiles.ymin; z <= affectedTiles.ymax; z++) {
					for (int x = affectedTiles.xmin; x <= affectedTiles.xmax; x++) {
						NavmeshTile tile = tiles[z*tileXCount + x];
						NavMeshGraph.UpdateArea(guo, tile);
					}
				}
				return;
			}

#if UNITY_2020_1_OR_NEWER
			// All updates will have been taken care of in UpdateAreaInit
			return;
#else
			Voxelize vox = globalVox;

			if (vox == null) {
				throw new System.InvalidOperationException("No Voxelizer object. UpdateAreaInit should have been called before this function.");
			}

			AstarProfiler.StartProfile("Build Tiles");

			var allMeshes = vox.inputMeshes;
			// Build the new tiles
			// If we are updating more than one tile it makes sense to do a more optimized pass for assigning each mesh to the buckets that it intersects.
			var buckets = PutMeshesIntoTileBuckets(vox.inputMeshes, affectedTiles);
			for (int x = affectedTiles.xmin; x <= affectedTiles.xmax; x++) {
				for (int z = affectedTiles.ymin; z <= affectedTiles.ymax; z++) {
					vox.inputMeshes = buckets[(z - affectedTiles.ymin)*affectedTiles.Width + (x - affectedTiles.xmin)];
					stagingTiles.Add(BuildTileMesh(vox, x, z));
				}
			}

			for (int i = 0; i < buckets.Length; i++) ListPool<Voxels.RasterizationMesh>.Release(buckets[i]);
			for (int i = 0; i < allMeshes.Count; i++) allMeshes[i].Pool();
			ListPool<Voxels.RasterizationMesh>.Release(ref allMeshes);
			vox.inputMeshes = null;

			uint graphIndex = (uint)AstarPath.active.data.GetGraphIndex(this);

			// Set the correct graph index
			for (int i = 0; i < stagingTiles.Count; i++) {
				NavmeshTile tile = stagingTiles[i];
				GraphNode[] nodes = tile.nodes;

				for (int j = 0; j < nodes.Length; j++) nodes[j].GraphIndex = graphIndex;
			}

			AstarProfiler.EndProfile("Build Tiles");
#endif
		}

		/// <summary>Called on the Unity thread to complete a graph update</summary>
		void IUpdatableGraph.UpdateAreaPost (GraphUpdateObject guo) {
#if UNITY_2020_1_OR_NEWER
			Profiler.BeginSample("Applying graph update results");
			var tilesResult = this.pendingGraphUpdatePromise2.Complete();
			var tileRect = tilesResult.dependency.tileMeshes.tileRect;
			var tiles = tilesResult.tiles;
			this.pendingGraphUpdatePromise.Dispose();
			this.pendingGraphUpdatePromise2.Dispose();
			this.pendingGraphUpdateArena.DisposeAll();

			// Assign all tiles to the graph.
			// Remove connections from existing tiles destroy the nodes
			// Replace the old tile by the new tile
			for (int z = 0; z < tileRect.Height; z++) {
				for (int x = 0; x < tileRect.Width; x++) {
					var tileIndex = (z+tileRect.ymin)*this.tileXCount + (x + tileRect.xmin);
					var oldTile = this.tiles[tileIndex];
					var newTile = tiles[z*tileRect.Width + x];

					// Destroy the previous nodes
					for (int j = 0; j < oldTile.nodes.Length; j++) {
						oldTile.nodes[j].Destroy();
					}

					// Assign the new tile
					newTile.graph = this;
					this.tiles[tileIndex] = newTile;
				}
			}

			// All tiles inside the update will already be connected to each other
			// but they will not be connected to any tiles outside the update.
			// We do this here. It needs to be done as one atomic update on the Unity main thread
			// because other code may be reading graph data on the main thread.
			var connectDependency = new JobHandle();
			var tilesHandle = System.Runtime.InteropServices.GCHandle.Alloc(this.tiles);
			var graphTileRect = new IntRect(0, 0, tileXCount - 1, tileZCount - 1);
			for (int z = tileRect.ymin; z <= tileRect.ymax; z++) {
				for (int x = tileRect.xmin; x <= tileRect.xmax; x++) {
					var tileIndex = z*graphTileRect.Width + x;
					var dep = new JobHandle();
					for (int direction = 0; direction < 4; direction++) {
						var nx = x + GridGraph.neighbourXOffsets[direction];
						var nz = z + GridGraph.neighbourZOffsets[direction];
						if (graphTileRect.Contains(nx, nz) && !tileRect.Contains(nx, nz)) {
							// Tile is contained in the graph but not in the graph update.
							// So we need to connect the tile inside the update to the one outside it.

							var ntileIndex = nz*graphTileRect.Width + nx;
							var job = new JobConnectTiles {
								tiles = tilesHandle,
								tileIndex1 = tileIndex,
								tileIndex2 = ntileIndex,
								tileWorldSizeX = TileWorldSizeX,
								tileWorldSizeZ = TileWorldSizeZ,
								maxTileConnectionEdgeDistance = MaxTileConnectionEdgeDistance,
							}.Schedule(dep);
							dep = JobHandle.CombineDependencies(dep, job);
						}
					}
					connectDependency = JobHandle.CombineDependencies(connectDependency, dep);
				}
			}
			connectDependency.Complete();

			// Signal that tiles have been recalculated to the navmesh cutting system.
			// This may be used to update the tile again to take into
			// account NavmeshCut components.
			// It is not the super efficient, but it works.
			// Usually you only use either normal graph updates OR navmesh
			// cutting, not both.
			navmeshUpdateData.OnRecalculatedTiles(tiles);
			if (OnRecalculatedTiles != null) OnRecalculatedTiles(tiles);


			Profiler.EndSample();
#else
			Profiler.BeginSample("RemoveConnections");
			// Remove connections from existing tiles destroy the nodes
			// Replace the old tile by the new tile
			for (int i = 0; i < stagingTiles.Count; i++) {
				var tile = stagingTiles[i];
				int index = tile.x + tile.z * tileXCount;
				var oldTile = tiles[index];

				// Destroy the previous nodes
				for (int j = 0; j < oldTile.nodes.Length; j++) {
					oldTile.nodes[j].Destroy();
				}

				tiles[index] = tile;
			}

			Profiler.EndSample();

			Profiler.BeginSample("Connect With Neighbours");
			// Connect the new tiles with their neighbours
			for (int i = 0; i < stagingTiles.Count; i++) {
				var tile = stagingTiles[i];
				ConnectTileWithNeighbours(tile, false);
			}

			// This may be used to update the tile again to take into
			// account NavmeshCut components.
			// It is not the super efficient, but it works.
			// Usually you only use either normal graph updates OR navmesh
			// cutting, not both.
			var updatedTiles = stagingTiles.ToArray();
			navmeshUpdateData.OnRecalculatedTiles(updatedTiles);
			if (OnRecalculatedTiles != null) OnRecalculatedTiles(updatedTiles);

			stagingTiles.Clear();
			Profiler.EndSample();
#endif
		}

		protected override IEnumerable<Progress> ScanInternal (bool async) {
			TriangleMeshNode.SetNavmeshHolder(AstarPath.active.data.GetGraphIndex(this), this);

			if (!Application.isPlaying) {
				RelevantGraphSurface.FindAllGraphSurfaces();
			}

			RelevantGraphSurface.UpdateAllPositions();

#if UNITY_2020_1_OR_NEWER
			foreach (var progress in ScanAllTilesBurst(async)) {
				yield return progress;
			}
#else
			Debug.LogWarning("The burstified recast code is only supported in Unity 2020.1 or newer. Falling back to the slower pure C# code.");
			foreach (var progress in ScanAllTiles()) {
				yield return progress;
			}
#endif
		}

		public override GraphTransform CalculateTransform () {
			return CalculateTransform(new Bounds(forcedBoundsCenter, forcedBoundsSize), Quaternion.Euler(rotation));
		}

		public static GraphTransform CalculateTransform (Bounds bounds, Quaternion rotation) {
			return new GraphTransform(Matrix4x4.TRS(bounds.center, rotation, Vector3.one) * Matrix4x4.TRS(-bounds.extents, Quaternion.identity, Vector3.one));
		}

		void InitializeTileInfo () {
			// Voxel grid size
			int totalVoxelWidth = (int)(forcedBoundsSize.x/cellSize + 0.5f);
			int totalVoxelDepth = (int)(forcedBoundsSize.z/cellSize + 0.5f);

			if (!useTiles) {
				tileSizeX = totalVoxelWidth;
				tileSizeZ = totalVoxelDepth;
			} else {
				tileSizeX = editorTileSize;
				tileSizeZ = editorTileSize;
			}

			// Number of tiles
			tileXCount = (totalVoxelWidth + tileSizeX-1) / tileSizeX;
			tileZCount = (totalVoxelDepth + tileSizeZ-1) / tileSizeZ;

			if (tileXCount * tileZCount > TileIndexMask+1) {
				throw new System.Exception("Too many tiles ("+(tileXCount * tileZCount)+") maximum is "+(TileIndexMask+1)+
					"\nTry disabling ASTAR_RECAST_LARGER_TILES under the 'Optimizations' tab in the A* inspector.");
			}

			// TODO: Unnecessary initialization when using burst
			tiles = new NavmeshTile[tileXCount*tileZCount];
		}

		/// <summary>Creates a list for every tile and adds every mesh that touches a tile to the corresponding list</summary>
		List<Voxels.RasterizationMesh>[] PutMeshesIntoTileBuckets (List<Voxels.RasterizationMesh> meshes, IntRect tileBuckets) {
			var bucketCount = tileBuckets.Width*tileBuckets.Height;
			var result = new List<Voxels.RasterizationMesh>[bucketCount];
			var borderExpansion = new Vector3(1, 0, 1)*TileBorderSizeInWorldUnits*2;

			for (int i = 0; i < result.Length; i++) {
				result[i] = ListPool<Voxels.RasterizationMesh>.Claim();
			}

			var offset = -tileBuckets.Min;
			var clamp = new IntRect(0, 0, tileBuckets.Width - 1, tileBuckets.Height - 1);
			for (int i = 0; i < meshes.Count; i++) {
				var mesh = meshes[i];
				var bounds = mesh.bounds;
				// Expand borderSize voxels on each side
				bounds.Expand(borderExpansion);

				var rect = GetTouchingTiles(bounds);
				rect = IntRect.Intersection(rect.Offset(offset), clamp);
				for (int z = rect.ymin; z <= rect.ymax; z++) {
					for (int x = rect.xmin; x <= rect.xmax; x++) {
						result[x + z*tileBuckets.Width].Add(mesh);
					}
				}
			}

			return result;
		}

		protected IEnumerable<Progress> ScanAllTilesBurst (bool async) {
			transform = CalculateTransform();
			InitializeTileInfo();
			var tileRect = new IntRect(0, 0, tileXCount - 1, tileZCount - 1);

			// If this is true, just fill the graph with empty tiles
			if (scanEmptyGraph || tileRect.Area <= 0) {
				FillWithEmptyTiles();
				yield break;
			}

			var arena = new DisposeArena();
			var tileMeshesPromise = RecastBuilder.BuildTileMeshes(this, tileRect).Schedule(arena);
			var tilesPromise = RecastBuilder.BuildNodeTiles(this).Schedule(arena, tileMeshesPromise);
			if (async) {
				while (!tilesPromise.IsCompleted) {
					System.Threading.Thread.Sleep(1);
					yield return tileMeshesPromise.Progress;
				}
			}
			var tiles = tilesPromise.Complete();
			var tileMeshes = tileMeshesPromise.Complete();

#if UNITY_EDITOR
			this.meshesUnreadableAtRuntime = tileMeshes.meshesUnreadableAtRuntime;
			tileMeshes.meshesUnreadableAtRuntime = null;
#endif

			// Assign all tiles to the graph
			for (int z = 0; z < tileRect.Height; z++) {
				for (int x = 0; x < tileRect.Width; x++) {
					var tile = tiles.tiles[z*tileRect.Width + x];
					tile.graph = this;
					this.tiles[(z+tileRect.ymin)*this.tileXCount + (x + tileRect.xmin)] = tile;
				}
			}

			tileMeshes.Dispose();
			tiles.Dispose();
			arena.DisposeAll();

			// Signal that tiles have been recalculated to the navmesh cutting system.
			navmeshUpdateData.OnRecalculatedTiles(this.tiles);
			if (OnRecalculatedTiles != null) OnRecalculatedTiles(this.tiles.Clone() as NavmeshTile[]);
		}

		/// <summary>Helper methods for scanning a recast graph</summary>
		public struct RecastBuilder {
			public class BuildNodeTilesOutput : IProgress, System.IDisposable {
				public BuildTileMeshesOutput dependency;
				public NavmeshTile[] tiles;

				public Progress Progress {
					get {
						return dependency.Progress;
					}
				}

				public void Dispose () {
				}
			}

			/// <summary>Represents a group of tiles of a recast graph</summary>
			public struct TileMeshes {
				/// <summary>Tiles laid out row by row</summary>
				public TileBuilderBurst.TileMesh[] tileMeshes;
				/// <summary>Which tiles in the graph this group of tiles represents</summary>
				public IntRect tileRect;
				/// <summary>World-space size of each tile</summary>
				public Vector2 tileWorldSize;

				/// <summary>Rotate this group of tiles by 90*N degrees clockwise about the group's center</summary>
				public void Rotate (int rotation) {
					rotation = -rotation;
					// Get the positive remainder modulo 4. I.e. a number between 0 and 3.
					rotation = ((rotation % 4) + 4) % 4;
					if (rotation == 0) return;
					var rot90 = new int2x2(0, -1, 1, 0);
					var rotN = int2x2.identity;
					for (int i = 0; i < rotation; i++) rotN = math.mul(rotN, rot90);

					var tileSize = (Int3) new Vector3(tileWorldSize.x, 0, tileWorldSize.y);
					var offset = -math.min(int2.zero, math.mul(rotN, new int2(tileSize.x, tileSize.z)));
					var size = new int2(tileRect.Width, tileRect.Height);
					var offsetTileCoordinate = -math.min(int2.zero, math.mul(rotN, size - 1));
					var newTileMeshes = new TileBuilderBurst.TileMesh[tileMeshes.Length];
					var newSize = (rotation % 2) == 0 ? size : new int2(size.y, size.x);

					for (int z = 0; z < size.y; z++) {
						for (int x = 0; x < size.x; x++) {
							var vertices = tileMeshes[x + z*size.x].verticesInTileSpace;
							for (int i = 0; i < vertices.Length; i++) {
								var v = vertices[i];
								var rotated = math.mul(rotN, new int2(v.x, v.z)) + offset;
								vertices[i] = new Int3(rotated.x, v.y, rotated.y);
							}

							var tileCoord = math.mul(rotN, new int2(x, z)) + offsetTileCoordinate;
							newTileMeshes[tileCoord.x + tileCoord.y*newSize.x] = tileMeshes[x + z*size.x];
						}
					}

					tileMeshes = newTileMeshes;
					tileWorldSize = rotation % 2 == 0 ? tileWorldSize : new Vector2(tileWorldSize.y, tileWorldSize.x);
					tileRect = new IntRect(tileRect.xmin, tileRect.ymin, tileRect.xmin + newSize.x - 1, tileRect.ymin + newSize.y - 1);
				}

				/// <summary>
				/// Serialize this struct to a portable byte array.
				/// The data is compressed using the deflate algorithm to reduce size.
				/// See: <see cref="Deserialize"/>
				/// </summary>
				public byte[] Serialize () {
					var buffer = new System.IO.MemoryStream();
					var writer = new System.IO.BinaryWriter(new System.IO.Compression.DeflateStream(buffer, System.IO.Compression.CompressionMode.Compress));
					// Version
					writer.Write(0);
					writer.Write(tileRect.Width);
					writer.Write(tileRect.Height);
					writer.Write(this.tileWorldSize.x);
					writer.Write(this.tileWorldSize.y);
					for (int z = 0; z < tileRect.Height; z++) {
						for (int x = 0; x < tileRect.Width; x++) {
							var tile = tileMeshes[(z*tileRect.Width) + x];
							UnityEngine.Assertions.Assert.IsTrue(tile.tags.Length*3 == tile.triangles.Length);
							writer.Write(tile.triangles.Length);
							writer.Write(tile.verticesInTileSpace.Length);
							for (int i = 0; i < tile.verticesInTileSpace.Length; i++) {
								var v = tile.verticesInTileSpace[i];
								writer.Write(v.x);
								writer.Write(v.y);
								writer.Write(v.z);
							}
							for (int i = 0; i < tile.triangles.Length; i++) writer.Write(tile.triangles[i]);
							for (int i = 0; i < tile.tags.Length; i++) writer.Write(tile.tags[i]);
						}
					}
					writer.Close();
					return buffer.ToArray();
				}

				/// <summary>
				/// Deserialize an instance from a byte array.
				/// See: <see cref="Serialize"/>
				/// </summary>
				public static TileMeshes Deserialize (byte[] bytes) {
					var reader = new System.IO.BinaryReader(new System.IO.Compression.DeflateStream(new System.IO.MemoryStream(bytes), System.IO.Compression.CompressionMode.Decompress));
					var version = reader.ReadInt32();
					if (version != 0) throw new System.Exception("Invalid data. Unexpected version number.");
					var w = reader.ReadInt32();
					var h = reader.ReadInt32();
					var tileSize = new Vector2(reader.ReadSingle(), reader.ReadSingle());
					if (w < 0 || h < 0) throw new System.Exception("Invalid bounds");

					var tileRect = new IntRect(0, 0, w - 1, h - 1);

					var tileMeshes = new TileBuilderBurst.TileMesh[w*h];
					for (int z = 0; z < h; z++) {
						for (int x = 0; x < w; x++) {
							int[] tris = new int[reader.ReadInt32()];
							Int3[] vertsInTileSpace = new Int3[reader.ReadInt32()];
							uint[] tags = new uint[tris.Length/3];

							for (int i = 0; i < vertsInTileSpace.Length; i++) vertsInTileSpace[i] = new Int3(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
							for (int i = 0; i < tris.Length; i++) {
								tris[i] = reader.ReadInt32();
								UnityEngine.Assertions.Assert.IsTrue(tris[i] >= 0 && tris[i] < vertsInTileSpace.Length);
							}
							for (int i = 0; i < tags.Length; i++) tags[i] = reader.ReadUInt32();

							tileMeshes[x + z*w] = new TileBuilderBurst.TileMesh {
								triangles = tris,
								verticesInTileSpace = vertsInTileSpace,
								tags = tags,
							};
						}
					}
					return new TileMeshes {
							   tileMeshes = tileMeshes,
							   tileRect = tileRect,
							   tileWorldSize = tileSize,
					};
				}
			}

			/// <summary>Unsafe representation of a <see cref="TileMeshes"/> struct</summary>
			public struct TileMeshesUnsafe {
				public NativeArray<TileBuilderBurst.TileMesh.TileMeshUnsafe> tileMeshes;
				public IntRect tileRect;
				public Vector2 tileWorldSize;

				public TileMeshesUnsafe(NativeArray<TileBuilderBurst.TileMesh.TileMeshUnsafe> tileMeshes, IntRect tileRect, Vector2 tileWorldSize) {
					this.tileMeshes = tileMeshes;
					this.tileRect = tileRect;
					this.tileWorldSize = tileWorldSize;
				}

				/// <summary>Copies the native data to managed data arrays which are easier to work with</summary>
				public TileMeshes ToManaged () {
					var output = new TileBuilderBurst.TileMesh[tileMeshes.Length];
					for (int i = 0; i < output.Length; i++) {
						output[i] = tileMeshes[i].ToManaged();
					}
					return new TileMeshes {
							   tileMeshes = output,
							   tileRect = this.tileRect,
							   tileWorldSize = this.tileWorldSize,
					};
				}

				public void Dispose () {
					for (int i = 0; i < tileMeshes.Length; i++) tileMeshes[i].Dispose();
					tileMeshes.Dispose();
				}
			}

			public class BuildTileMeshesOutput : IProgress, System.IDisposable {
				public NativeArray<int> currentTileCounter;
				public TileMeshesUnsafe tileMeshes;
#if UNITY_EDITOR
				public List<Mesh> meshesUnreadableAtRuntime;
#endif

				public Progress Progress {
					get {
						var tileCount = tileMeshes.tileRect.Area;
						var currentTile = Mathf.Min(tileCount, currentTileCounter[0]);
						return new Progress(tileCount > 0 ? currentTile / (float)tileCount : 0, "Scanning tiles: " + currentTile + " of " + (tileCount) + " tiles...");
					}
				}

				public void Dispose () {
					tileMeshes.Dispose();
					currentTileCounter.Dispose();
#if UNITY_EDITOR
					if (meshesUnreadableAtRuntime != null) ListPool<Mesh>.Release(ref meshesUnreadableAtRuntime);
#endif
				}
			}

			/// <summary>
			/// Builds meshes for the given tiles in a graph.
			/// Call Schedule on the returned object to actually start the job.
			/// </summary>
			public static BuildTilesSettings BuildTileMeshes (RecastGraph graph, IntRect tileRect) {
				var settings = new BuildTilesSettings(graph);
				settings.tileRect = tileRect;
				return settings;
			}

			/// <summary>
			/// Builds nodes given some tile meshes.
			/// Call Schedule on the returned object to actually start the job.
			///
			/// See: <see cref="BuildTileMeshes"/>
			/// </summary>
			public static BuildNodesJob BuildNodeTiles (RecastGraph graph) {
				return new BuildNodesJob(graph);
			}
		}

		/// <summary>
		/// Settings for building tile meshes in a recast graph.
		/// See: <see cref="RecastGraph"/> for more documentation on the individual fields.
		/// See: <see cref="RecastBuilder"/>
		/// </summary>
		public struct BuildTilesSettings {
			public GraphTransform transform;
			public IntRect tileRect;
			public float walkableClimb;
			/// <summary>Size of bounds along the y axis in graph space (i.e. 'up' direction)</summary>
			public float boundsYSize;
			public int terrainSampleSize;
			public LayerMask mask;
			public List<string> tagMask;
			public RelevantGraphSurfaceMode relevantGraphSurfaceMode;
			public float colliderRasterizeDetail;
			public int tileSizeX;
			public int tileSizeZ;
			public float cellSize;

			public bool rasterizeTerrain;
			public bool rasterizeMeshes;
			public bool rasterizeTrees;
			public bool rasterizeColliders;
			// TODO: Don't store in struct
			public int tileBorderSizeInVoxels;
			public float walkableHeight;
			public float maxSlope;
			// TODO: Specify in world units
			public int characterRadiusInVoxels;
			public int minRegionSize;
			public float maxEdgeLength;
			public float contourMaxError;
			public UnityEngine.SceneManagement.Scene scene;

			public void SetWorldSpaceBounds (Bounds bounds, Quaternion rotation) {
				this.transform = CalculateTransform(bounds, rotation);

				int totalVoxelWidth = (int)(bounds.size.x/cellSize + 0.5f);
				int totalVoxelDepth = (int)(bounds.size.z/cellSize + 0.5f);
				// Number of tiles
				tileRect = new IntRect(
					0,
					0,
					Mathf.Max(0, (totalVoxelWidth + tileSizeX-1) / tileSizeX - 1),
					Mathf.Max(0, (totalVoxelDepth + tileSizeZ-1) / tileSizeZ - 1)
					);

				if (tileRect.Area > TileIndexMask+1) {
					throw new System.Exception("Too many tiles ("+tileRect.Area+") maximum is "+(TileIndexMask+1)+
						"\nTry disabling ASTAR_RECAST_LARGER_TILES under the 'Optimizations' tab in the A* inspector.");
				}

				this.boundsYSize = bounds.size.y;
			}

			public BuildTilesSettings (RecastGraph graph) {
				// A walkableClimb higher than walkableHeight can cause issues when generating the navmesh since then it can in some cases
				// Both be valid for a character to walk under an obstacle and climb up on top of it (and that cannot be handled with navmesh without links)
				// The editor scripts also enforce this but we enforce it here too just to be sure
				this.walkableClimb = Mathf.Min(graph.walkableClimb, graph.walkableHeight);

				this.terrainSampleSize = graph.terrainSampleSize;
				this.mask = graph.mask;
				this.tagMask = graph.tagMask;
				this.colliderRasterizeDetail = graph.colliderRasterizeDetail;
				this.rasterizeTerrain = graph.rasterizeTerrain;
				this.rasterizeMeshes = graph.rasterizeMeshes;
				this.rasterizeTrees = graph.rasterizeTrees;
				this.rasterizeColliders = graph.rasterizeColliders;
				this.cellSize = graph.cellSize;
				this.tileBorderSizeInVoxels = graph.TileBorderSizeInVoxels;
				this.walkableHeight = graph.walkableHeight;
				this.maxSlope = graph.maxSlope;
				this.characterRadiusInVoxels = graph.CharacterRadiusInVoxels;
				this.minRegionSize = Mathf.RoundToInt(graph.minRegionSize);
				this.maxEdgeLength = graph.maxEdgeLength;
				this.contourMaxError = graph.contourMaxError;
				this.tileSizeX = graph.editorTileSize;
				this.tileSizeZ = graph.editorTileSize;
				this.relevantGraphSurfaceMode = graph.relevantGraphSurfaceMode;
				this.scene = graph.active.gameObject.scene;
				this.transform = default;
				this.tileRect = default;
				this.boundsYSize = default;
				this.SetWorldSpaceBounds(new Bounds(graph.forcedBoundsCenter, graph.forcedBoundsSize), Quaternion.Euler(graph.rotation));
				if (!graph.useTiles) {
					this.tileSizeX = this.tileSizeX * this.tileRect.Width;
					this.tileSizeZ = this.tileSizeZ * this.tileRect.Height;
					this.tileRect = new IntRect(0, 0, 0, 0);
				}
			}

			/// <summary>
			/// Voxel y coordinates will be stored as ushorts which have 65536 values.
			/// Leave a margin to make sure things do not overflow
			/// </summary>
			float CellHeight => Mathf.Max(boundsYSize / 64000, 0.001f);

			float TileWorldSizeX => tileSizeX * cellSize;
			float TileWorldSizeZ => tileSizeZ * cellSize;

			/// <summary>
			/// Number of extra voxels on each side of a tile to ensure accurate navmeshes near the tile border.
			/// The width of a tile is expanded by 2 times this value (1x to the left and 1x to the right)
			/// </summary>
			int TileBorderSizeInVoxels {
				get {
					return characterRadiusInVoxels + 3;
				}
			}

			float TileBorderSizeInWorldUnits {
				get {
					return TileBorderSizeInVoxels*cellSize;
				}
			}

			/// <summary>Returns an XZ bounds object with the bounds of a group of tiles in graph space</summary>
			Bounds GetTileBoundsInGraphSpace (int x, int z, int width = 1, int depth = 1) {
				var bounds = new Bounds();

				bounds.SetMinMax(new Vector3(x*TileWorldSizeX, 0, z*TileWorldSizeZ),
					new Vector3((x+width)*TileWorldSizeX, this.boundsYSize, (z+depth)*TileWorldSizeZ)
					);

				return bounds;
			}

			/// <summary>
			/// Returns a rect containing the indices of all tiles touching the specified bounds.
			/// If a margin is passed, the bounding box in graph space is expanded by that amount in every direction.
			/// </summary>
			IntRect GetTouchingTiles (Bounds bounds, float margin = 0) {
				bounds = transform.InverseTransform(bounds);

				// Calculate world bounds of all affected tiles
				return new IntRect(Mathf.FloorToInt((bounds.min.x - margin) / TileWorldSizeX), Mathf.FloorToInt((bounds.min.z - margin) / TileWorldSizeZ), Mathf.FloorToInt((bounds.max.x + margin) / TileWorldSizeX), Mathf.FloorToInt((bounds.max.z + margin) / TileWorldSizeZ));
			}

			RecastMeshGathererBurst.MeshCollection CollectMeshes () {
				var boundsWorldSpace = transform.Transform(this.GetTileBoundsInGraphSpace(tileRect.xmin, tileRect.ymin, tileRect.Width, tileRect.Height));
				// Expand borderSize voxels on each side
				var bounds = boundsWorldSpace;
				var borderExpansion = new Vector3(1, 0, 1)*TileBorderSizeInWorldUnits*2;

				bounds.Expand(borderExpansion);

				Profiler.BeginSample("Find Meshes for rasterization");
				var meshGatherer = new RecastMeshGathererBurst(scene, bounds, terrainSampleSize, mask, tagMask, colliderRasterizeDetail);

				if (rasterizeMeshes) {
					Profiler.BeginSample("Find meshes");
					meshGatherer.CollectSceneMeshes();
					Profiler.EndSample();
				}

				Profiler.BeginSample("Find RecastMeshObj components");
				meshGatherer.CollectRecastMeshObjs();
				Profiler.EndSample();

				if (rasterizeTerrain) {
					Profiler.BeginSample("Find terrains");
					// Split terrains up into meshes approximately the size of a single chunk
					var desiredTerrainChunkSize = cellSize*Math.Max(tileSizeX, tileSizeZ);
					meshGatherer.CollectTerrainMeshes(rasterizeTrees, desiredTerrainChunkSize);
					Profiler.EndSample();
				}

				if (rasterizeColliders) {
					Profiler.BeginSample("Find colliders");
					meshGatherer.CollectColliderMeshes();
					Profiler.EndSample();
				}

				Profiler.BeginSample("Finalizing");
				var result = meshGatherer.Finalize();
				Profiler.EndSample();

				if (result.meshes.Length == 0) {
					Debug.LogWarning("No rasterizable objects were found contained in the layers specified by the 'mask' variables");
				}

				Profiler.EndSample();
				return result;
			}

			public struct BucketMapping {
				public NativeArray<Voxels.Burst.RasterizationMesh> meshes;
				public NativeArray<int> pointers;
				public NativeArray<int> bucketRanges;
			}

			/// <summary>Creates a list for every tile and adds every mesh that touches a tile to the corresponding list</summary>
			BucketMapping PutMeshesIntoTileBuckets (RecastMeshGathererBurst.MeshCollection meshCollection, IntRect tileBuckets) {
				var bucketCount = tileBuckets.Width*tileBuckets.Height;
				var buckets = new NativeList<int>[bucketCount];
				var borderExpansion = new Vector3(1, 0, 1)*TileBorderSizeInWorldUnits*2;

				for (int i = 0; i < buckets.Length; i++) {
					buckets[i] = new NativeList<int>(Allocator.Persistent);
				}

				var offset = -tileBuckets.Min;
				var clamp = new IntRect(0, 0, tileBuckets.Width - 1, tileBuckets.Height - 1);
				var meshes = meshCollection.meshes;
				for (int i = 0; i < meshes.Length; i++) {
					var mesh = meshes[i];
					var bounds = mesh.bounds;
					// Expand borderSize voxels on each side
					bounds.Expand(borderExpansion);

					var rect = GetTouchingTiles(bounds);
					rect = IntRect.Intersection(rect.Offset(offset), clamp);
					for (int z = rect.ymin; z <= rect.ymax; z++) {
						for (int x = rect.xmin; x <= rect.xmax; x++) {
							buckets[x + z*tileBuckets.Width].Add(i);
						}
					}
				}

				// Concat buckets
				int allPointersCount = 0;
				for (int i = 0; i < buckets.Length; i++) allPointersCount += buckets[i].Length;
				var allPointers = new NativeArray<int>(allPointersCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
				var bucketRanges = new NativeArray<int>(bucketCount+1, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
				allPointersCount = 0;
				for (int i = 0; i < buckets.Length; i++) {
					bucketRanges[i] = allPointersCount;
					// If we have an empty bucket at the end of the array then allPointersCount might be equal to allPointers.Length which would cause an assert to trigger.
					// So for empty buckets don't call the copy method
					if (buckets[i].Length > 0) {
						NativeArray<int>.Copy(buckets[i], 0, allPointers, allPointersCount, buckets[i].Length);
					}
					allPointersCount += buckets[i].Length;
					buckets[i].Dispose();
				}
				bucketRanges[buckets.Length] = allPointersCount;

				return new BucketMapping {
						   meshes = meshCollection.meshes,
						   pointers = allPointers,
						   bucketRanges = bucketRanges,
				};
			}

			public Promise<RecastBuilder.BuildTileMeshesOutput> Schedule (DisposeArena arena) {
				var tileCount = tileRect.Area;

				var tileRectWidth = tileRect.Width;
				var tileRectDepth = tileRect.Height;

				var meshes = this.CollectMeshes();

				Profiler.BeginSample("PutMeshesIntoTileBuckets");
				var buckets = PutMeshesIntoTileBuckets(meshes, tileRect);
				Profiler.EndSample();

				Profiler.BeginSample("Allocating tiles");
				var tileMeshes = new NativeArray<TileBuilderBurst.TileMesh.TileMeshUnsafe>(tileCount, Allocator.Persistent, NativeArrayOptions.ClearMemory);

				int width = tileSizeX + tileBorderSizeInVoxels*2;
				int depth = tileSizeZ + tileBorderSizeInVoxels*2;
				var cellHeight = CellHeight;
				// TODO: Move inside BuildTileMeshBurst
				var voxelWalkableHeight = (uint)(walkableHeight/cellHeight);
				var voxelWalkableClimb = Mathf.RoundToInt(walkableClimb/cellHeight);

				var tileGraphSpaceBounds = new NativeArray<Bounds>(tileCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

				for (int z = 0; z < tileRectDepth; z++) {
					for (int x = 0; x < tileRectWidth; x++) {
						int tileIndex = x + z*tileRectWidth;
						var tileBounds = GetTileBoundsInGraphSpace(tileRect.xmin + x, tileRect.ymin + z);
						// Expand borderSize voxels on each side
						tileBounds.Expand(new Vector3(1, 0, 1)*TileBorderSizeInWorldUnits*2);
						tileGraphSpaceBounds[tileIndex] = tileBounds;
					}
				}

				Profiler.EndSample();
				Profiler.BeginSample("Scheduling jobs");

				var builders = new TileBuilderBurst[Mathf.Max(1, Mathf.Min(tileCount, Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount + 1))];
				var currentTileCounter = new NativeArray<int>(1, Allocator.Persistent);
				JobHandle dependencies = default;

				var relevantGraphSurfaces = new NativeList<JobBuildRegions.RelevantGraphSurfaceInfo>(Allocator.Persistent);
				var c = RelevantGraphSurface.Root;
				while (c != null) {
					relevantGraphSurfaces.Add(new JobBuildRegions.RelevantGraphSurfaceInfo {
						position = c.transform.position,
						range = c.maxRange,
					});
					c = c.Next;
				}


				// Having a few long running jobs is bad because Unity cannot inject more high priority jobs
				// in between tile calculations. So we run each builder a number of times.
				// Each step will just calculate one tile.
				int tilesPerJob = Mathf.CeilToInt(Mathf.Sqrt(tileCount));
				// Number of tiles calculated if every builder runs once
				int tilesPerStep = tilesPerJob * builders.Length;
				// Round up to make sure we run the jobs enough times
				// We multiply by 2 to run a bit more jobs than strictly necessary.
				// This is to ensure that if one builder just gets a bunch of long running jobs
				// then the other builders can steal some work from it.
				int jobSteps = 2 * (tileCount + tilesPerStep - 1) / tilesPerStep;
				var jobTemplate = new JobBuildTileMesh {
					tileBuilder = builders[0],
					inputMeshes = buckets,
					tileGraphSpaceBounds = tileGraphSpaceBounds,
					voxelWalkableClimb = voxelWalkableClimb,
					voxelWalkableHeight = voxelWalkableHeight,
					voxelToTileSpace = Matrix4x4.Scale(new Vector3(cellSize, cellHeight, cellSize)) * Matrix4x4.Translate(-new Vector3(1, 0, 1)*TileBorderSizeInVoxels),
					cellSize = cellSize,
					cellHeight = cellHeight,
					maxSlope = maxSlope,
					graphToWorldSpace = transform.matrix,
					characterRadiusInVoxels = characterRadiusInVoxels,
					tileBorderSizeInVoxels = tileBorderSizeInVoxels,
					minRegionSize = minRegionSize,
					maxEdgeLength = maxEdgeLength,
					contourMaxError = contourMaxError,
					maxTiles = tilesPerJob,
					relevantGraphSurfaces = relevantGraphSurfaces.AsArray(),
					relevantGraphSurfaceMode = this.relevantGraphSurfaceMode,
				};
				jobTemplate.SetOutputMeshes(tileMeshes);
				jobTemplate.SetCounter(currentTileCounter);
				for (int i = 0; i < builders.Length; i++) {
					jobTemplate.tileBuilder = builders[i] = new TileBuilderBurst(width, depth, (int)voxelWalkableHeight);
					var dep = new JobHandle();
					for (int j = 0; j < jobSteps; j++) {
						dep = jobTemplate.Schedule(dep);
					}
					dependencies = JobHandle.CombineDependencies(dependencies, dep);
				}
				JobHandle.ScheduleBatchedJobs();

				Profiler.EndSample();

				arena.Add(tileGraphSpaceBounds);
				arena.Add(relevantGraphSurfaces);
				arena.Add(buckets.bucketRanges);
				arena.Add(buckets.pointers);
				// Note: buckets.meshes references data in #meshes, so we don't have to dispose it separately
				arena.Add(meshes);

				// Dispose the mesh data after all jobs are completed.
				// Note that the jobs use pointers to this data which are not tracked by the safety system.
				for (int i = 0; i < builders.Length; i++) arena.Add(builders[i]);

				return new Promise<RecastBuilder.BuildTileMeshesOutput>(dependencies, new RecastBuilder.BuildTileMeshesOutput {
					tileMeshes = new RecastBuilder.TileMeshesUnsafe(tileMeshes, tileRect, new Vector2(TileWorldSizeX, TileWorldSizeZ)),
					currentTileCounter = currentTileCounter,
#if UNITY_EDITOR
					meshesUnreadableAtRuntime = meshes.meshesUnreadableAtRuntime,
#endif
				});
			}
		}

		public struct BuildNodesJob {
			AstarPath astar;
			IntRect graphTileRect;
			uint graphIndex;
			public uint initialPenalty;
			public bool recalculateNormals;
			public float maxTileConnectionEdgeDistance;
			Vector2 tileWorldSize;
			Matrix4x4 graphToWorldSpace;

			public BuildNodesJob(RecastGraph graph) {
				this.astar = graph.active;
				this.graphTileRect = new IntRect(0, 0, graph.tileXCount - 1, graph.tileZCount - 1);
				this.graphIndex = graph.graphIndex;
				this.initialPenalty = graph.initialPenalty;
				this.recalculateNormals = graph.RecalculateNormals;
				this.maxTileConnectionEdgeDistance = graph.MaxTileConnectionEdgeDistance;
				this.tileWorldSize = new Vector2(graph.TileWorldSizeX, graph.TileWorldSizeZ);
				this.graphToWorldSpace = graph.transform.matrix;
			}

			public Promise<RecastBuilder.BuildNodeTilesOutput> Schedule (DisposeArena arena, Promise<RecastBuilder.BuildTileMeshesOutput> dependency) {
				var input = dependency.GetValue();
				var tileRect = input.tileMeshes.tileRect;
				UnityEngine.Assertions.Assert.AreEqual(input.tileMeshes.tileMeshes.Length, tileRect.Area);
				var tiles = new NavmeshTile[tileRect.Area];
				var astarGCHandle = System.Runtime.InteropServices.GCHandle.Alloc(astar);
				var tilesGCHandle = System.Runtime.InteropServices.GCHandle.Alloc(tiles);
				var createTilesJob = new CreateTilesJob {
					tileMeshes = input.tileMeshes.tileMeshes,
					tiles = tilesGCHandle,
					astar = astarGCHandle,
					tileRect = tileRect,
					graphTileRect = graphTileRect,
					graphIndex = graphIndex,
					initialPenalty = initialPenalty,
					recalculateNormals = recalculateNormals,
					graphToWorldSpace = this.graphToWorldSpace,
					tileWorldSize = this.tileWorldSize,
				}.Schedule(dependency.handle);

				Profiler.BeginSample("Scheduling ConnectTiles");
				var connectTilesDependency = JobConnectTiles.ScheduleBatch(tilesGCHandle, createTilesJob, tileRect, tileWorldSize, maxTileConnectionEdgeDistance);
				Profiler.EndSample();

				arena.Add(astarGCHandle);
				arena.Add(tilesGCHandle);
				return new Promise<RecastBuilder.BuildNodeTilesOutput>(connectTilesDependency, new RecastBuilder.BuildNodeTilesOutput {
					dependency = input,
					tiles = tiles,
				});
			}
		}

		struct CreateTilesJob : IJob {
			public NativeArray<TileBuilderBurst.TileMesh.TileMeshUnsafe> tileMeshes;
			public System.Runtime.InteropServices.GCHandle astar;
			public System.Runtime.InteropServices.GCHandle tiles;
			public uint graphIndex;
			public IntRect graphTileRect;
			public IntRect tileRect;
			public uint initialPenalty;
			public bool recalculateNormals;
			public Vector2 tileWorldSize;
			public Matrix4x4 graphToWorldSpace;

			public void Execute () {
				var tiles = (NavmeshTile[])this.tiles.Target;
				var tileRectWidth = tileRect.Width;
				var tileRectDepth = tileRect.Height;

				for (int z = 0; z < tileRectDepth; z++) {
					for (int x = 0; x < tileRectWidth; x++) {
						var tileIndex = z*tileRectWidth + x;
						// If we are just updating a part of the graph we still want to assign the nodes the proper global tile index
						var graphTileIndex = (z + tileRect.ymin)*graphTileRect.Width + (x + tileRect.xmin);
						var mesh = tileMeshes[tileIndex].ToManaged();

						// Convert tile space to graph space and world space
						var verticesInGraphSpace = mesh.verticesInTileSpace;
						var verticesInWorldSpace = new Int3[verticesInGraphSpace.Length];
						var tileSpaceToGraphSpaceOffset = (Int3) new Vector3(tileWorldSize.x * (x + tileRect.xmin), 0, tileWorldSize.y * (z + tileRect.ymin));
						for (int i = 0; i < verticesInGraphSpace.Length; i++) {
							var v = verticesInGraphSpace[i] + tileSpaceToGraphSpaceOffset;
							verticesInGraphSpace[i] = v;
							verticesInWorldSpace[i] = (Int3)graphToWorldSpace.MultiplyPoint3x4((Vector3)v);
						}

						// Create a new navmesh tile and assign its settings
						var tile = new NavmeshTile {
							x = x + tileRect.xmin,
							z = z + tileRect.ymin,
							w = 1,
							d = 1,
							tris = mesh.triangles,
							vertsInGraphSpace = verticesInGraphSpace,
							verts = verticesInWorldSpace,
							bbTree = new BBTree(),
							// Leave empty for now, it will be filled in later
							graph = null,
						};

						tile.nodes = new TriangleMeshNode[tile.tris.Length/3];
						// We need to lock here because creating nodes is not thread safe
						// and we may be doing this from multiple threads at the same time
						lock (astar.Target) {
							Profiler.BeginSample("CreateNodes");
							CreateNodes(tile, tile.tris, graphTileIndex, graphIndex, mesh.tags, (AstarPath)astar.Target, initialPenalty, recalculateNormals);
							Profiler.EndSample();
						}
						tile.bbTree.RebuildFrom(tile);
						CreateNodeConnections(tile.nodes);
						tiles[tileIndex] = tile;
					}
				}
			}
		}


		protected IEnumerable<Progress> ScanAllTiles () {
			transform = CalculateTransform();
			InitializeTileInfo();

			// If this is true, just fill the graph with empty tiles
			if (scanEmptyGraph) {
				FillWithEmptyTiles();
				yield break;
			}

			// A walkableClimb higher than walkableHeight can cause issues when generating the navmesh since then it can in some cases
			// Both be valid for a character to walk under an obstacle and climb up on top of it (and that cannot be handled with navmesh without links)
			// The editor scripts also enforce this but we enforce it here too just to be sure
			walkableClimb = Mathf.Min(walkableClimb, walkableHeight);

			yield return new Progress(0, "Finding Meshes");
			var bounds = transform.Transform(new Bounds(forcedBoundsSize*0.5f, forcedBoundsSize));
			var meshes = CollectMeshes(bounds);
			var buckets = PutMeshesIntoTileBuckets(meshes, new IntRect(0, 0, tileXCount - 1, tileZCount - 1));

			Queue<Int2> tileQueue = new Queue<Int2>();

			// Put all tiles in the queue
			for (int z = 0; z < tileZCount; z++) {
				for (int x = 0; x < tileXCount; x++) {
					tileQueue.Enqueue(new Int2(x, z));
				}
			}

			var workQueue = new ParallelWorkQueue<Int2>(tileQueue);
			// Create the voxelizers and set all settings (one for each thread)
			var voxelizers = new Voxelize[workQueue.threadCount];
			for (int i = 0; i < voxelizers.Length; i++) voxelizers[i] = new Voxelize(CellHeight, cellSize, walkableClimb, walkableHeight, maxSlope, maxEdgeLength);
			workQueue.action = (tile, threadIndex) => {
				voxelizers[threadIndex].inputMeshes = buckets[tile.x + tile.y*tileXCount];
				tiles[tile.x + tile.y*tileXCount] = BuildTileMesh(voxelizers[threadIndex], tile.x, tile.y, threadIndex);
			};

			// Prioritize responsiveness while playing
			// but when not playing prioritize throughput
			// (the Unity progress bar is also pretty slow to update)
			int timeoutMillis = Application.isPlaying ? 1 : 200;

			// Scan all tiles in parallel
			foreach (var done in workQueue.Run(timeoutMillis)) {
				yield return new Progress(Mathf.Lerp(0.1f, 0.9f, done / (float)tiles.Length), "Calculated Tiles: " + done + "/" + tiles.Length);
			}

			yield return new Progress(0.9f, "Assigning Graph Indices");

			// Assign graph index to nodes
			uint graphIndex = (uint)AstarPath.active.data.GetGraphIndex(this);

			GetNodes(node => node.GraphIndex = graphIndex);

			// First connect all tiles with an EVEN coordinate sum
			// This would be the white squares on a chess board.
			// Then connect all tiles with an ODD coordinate sum (which would be all black squares on a chess board).
			// This will prevent the different threads that do all
			// this in parallel from conflicting with each other.
			// The directions are also done separately
			// first they are connected along the X direction and then along the Z direction.
			// Looping over 0 and then 1
			for (int coordinateSum = 0; coordinateSum <= 1; coordinateSum++) {
				for (int direction = 0; direction <= 1; direction++) {
					for (int i = 0; i < tiles.Length; i++) {
						if ((tiles[i].x + tiles[i].z) % 2 == coordinateSum) {
							tileQueue.Enqueue(new Int2(tiles[i].x, tiles[i].z));
						}
					}

					workQueue = new ParallelWorkQueue<Int2>(tileQueue);
					workQueue.action = (tile, threadIndex) => {
						// Connect with tile at (x+1,z) and (x,z+1)
						if (direction == 0 && tile.x < tileXCount - 1)
							ConnectTiles(tiles[tile.x + tile.y * tileXCount], tiles[tile.x + 1 + tile.y * tileXCount], TileWorldSizeX, TileWorldSizeZ, MaxTileConnectionEdgeDistance);
						if (direction == 1 && tile.y < tileZCount - 1)
							ConnectTiles(tiles[tile.x + tile.y * tileXCount], tiles[tile.x + (tile.y + 1) * tileXCount], TileWorldSizeX, TileWorldSizeZ, MaxTileConnectionEdgeDistance);
					};

					var numTilesInQueue = tileQueue.Count;
					// Connect all tiles in parallel
					foreach (var done in workQueue.Run(timeoutMillis)) {
						yield return new Progress(0.95f, "Connected Tiles " + (numTilesInQueue - done) + "/" + numTilesInQueue + " (Phase " + (direction + 1 + 2*coordinateSum) + " of 4)");
					}
				}
			}

			for (int i = 0; i < meshes.Count; i++) meshes[i].Pool();
			ListPool<Voxels.RasterizationMesh>.Release(ref meshes);

			// Signal that tiles have been recalculated to the navmesh cutting system.
			navmeshUpdateData.OnRecalculatedTiles(tiles);
			if (OnRecalculatedTiles != null) OnRecalculatedTiles(tiles.Clone() as NavmeshTile[]);
		}

		List<Voxels.RasterizationMesh> CollectMeshes (Bounds bounds) {
			Profiler.BeginSample("Find Meshes for rasterization");
			var result = ListPool<Voxels.RasterizationMesh>.Claim();

			var meshGatherer = new RecastMeshGatherer(bounds, terrainSampleSize, mask, tagMask, colliderRasterizeDetail);

			if (rasterizeMeshes) {
				Profiler.BeginSample("Find meshes");
				meshGatherer.CollectSceneMeshes(result);
				Profiler.EndSample();
			}

			Profiler.BeginSample("Find RecastMeshObj components");
			meshGatherer.CollectRecastMeshObjs(result);
			Profiler.EndSample();

			if (rasterizeTerrain) {
				Profiler.BeginSample("Find terrains");
				// Split terrains up into meshes approximately the size of a single chunk
				var desiredTerrainChunkSize = cellSize*Math.Max(tileSizeX, tileSizeZ);
				meshGatherer.CollectTerrainMeshes(rasterizeTrees, desiredTerrainChunkSize, result);
				Profiler.EndSample();
			}

			if (rasterizeColliders) {
				Profiler.BeginSample("Find colliders");
				meshGatherer.CollectColliderMeshes(result);
				Profiler.EndSample();
			}

			if (result.Count == 0) {
				Debug.LogWarning("No rasterizable objects were found contained in the layers specified by the 'mask' variables");
			}

			Profiler.EndSample();
			return result;
		}

		float CellHeight {
			get {
				// Voxel y coordinates will be stored as ushorts which have 65536 values
				// Leave a margin to make sure things do not overflow
				return Mathf.Max(forcedBoundsSize.y / 64000, 0.001f);
			}
		}

		/// <summary>Convert character radius to a number of voxels</summary>
		int CharacterRadiusInVoxels {
			get {
				// Round it up most of the time, but round it down
				// if it is very close to the result when rounded down
				return Mathf.CeilToInt((characterRadius / cellSize) - 0.1f);
			}
		}

		/// <summary>
		/// Number of extra voxels on each side of a tile to ensure accurate navmeshes near the tile border.
		/// The width of a tile is expanded by 2 times this value (1x to the left and 1x to the right)
		/// </summary>
		int TileBorderSizeInVoxels {
			get {
				return CharacterRadiusInVoxels + 3;
			}
		}

		float TileBorderSizeInWorldUnits {
			get {
				return TileBorderSizeInVoxels*cellSize;
			}
		}

		Bounds CalculateTileBoundsWithBorder (int x, int z) {
			var bounds = new Bounds();

			bounds.SetMinMax(new Vector3(x*TileWorldSizeX, 0, z*TileWorldSizeZ),
				new Vector3((x+1)*TileWorldSizeX, forcedBoundsSize.y, (z+1)*TileWorldSizeZ)
				);

			// Expand borderSize voxels on each side
			bounds.Expand(new Vector3(1, 0, 1)*TileBorderSizeInWorldUnits*2);
			return bounds;
		}

		public struct TileBuilderBurst : IArenaDisposable {
			public LinkedVoxelField linkedVoxelField;
			public CompactVoxelField compactVoxelField;
			public NativeList<ushort> distanceField;
			public NativeQueue<Int3> tmpQueue1;
			public NativeQueue<Int3> tmpQueue2;
			public NativeList<Voxels.Burst.VoxelContour> contours;
			public NativeList<int> contourVertices;
			public Pathfinding.Voxels.Burst.VoxelMesh voxelMesh;

			public struct TileMesh {
				public int[] triangles;
				public Int3[] verticesInTileSpace;
				/// One tag per triangle
				public uint[] tags;

				public struct TileMeshUnsafe {
					public Unity.Collections.LowLevel.Unsafe.UnsafeAppendBuffer triangles;
					public Unity.Collections.LowLevel.Unsafe.UnsafeAppendBuffer verticesInTileSpace;
					/// One tag per triangle
					public Unity.Collections.LowLevel.Unsafe.UnsafeAppendBuffer tags;

					public void Dispose () {
						triangles.Dispose();
						verticesInTileSpace.Dispose();
						tags.Dispose();
					}

					public TileMesh ToManaged () {
						return new TileMesh {
								   triangles = Memory.UnsafeAppendBufferToArray<int>(triangles),
								   verticesInTileSpace = Memory.UnsafeAppendBufferToArray<Int3>(verticesInTileSpace),
								   tags = Memory.UnsafeAppendBufferToArray<uint>(tags),
						};
					}
				}
			}

			public TileBuilderBurst (int width, int depth, int voxelWalkableHeight) {
				linkedVoxelField = new LinkedVoxelField(width, depth);
				compactVoxelField = new CompactVoxelField(width, depth, voxelWalkableHeight, Allocator.Persistent);
				tmpQueue1 = new NativeQueue<Int3>(Allocator.Persistent);
				tmpQueue2 = new NativeQueue<Int3>(Allocator.Persistent);
				distanceField = new NativeList<ushort>(0, Allocator.Persistent);
				contours = new NativeList<Voxels.Burst.VoxelContour>(Allocator.Persistent);
				contourVertices = new NativeList<int>(Allocator.Persistent);
				voxelMesh = new Pathfinding.Voxels.Burst.VoxelMesh {
					verts = new NativeList<Int3>(Allocator.Persistent),
					tris = new NativeList<int>(Allocator.Persistent),
					areas = new NativeList<int>(Allocator.Persistent),
				};
			}

			void IArenaDisposable.DisposeWith (DisposeArena arena) {
				arena.Add(linkedVoxelField);
				arena.Add(compactVoxelField);
				arena.Add(distanceField);
				arena.Add(tmpQueue1);
				arena.Add(tmpQueue2);
				arena.Add(contours);
				arena.Add(contourVertices);
				arena.Add(voxelMesh);
			}
		}

		[BurstCompile(CompileSynchronously = true)]
		// TODO: [BurstCompile(FloatMode = FloatMode.Fast)]
		struct JobBuildTileMesh : IJob {
			public TileBuilderBurst tileBuilder;
			[ReadOnly]
			public BuildTilesSettings.BucketMapping inputMeshes;
			[ReadOnly]
			public NativeArray<Bounds> tileGraphSpaceBounds;
			public Matrix4x4 voxelToTileSpace;

			[NativeDisableUnsafePtrRestriction]
			public unsafe TileBuilderBurst.TileMesh.TileMeshUnsafe* outputMeshes;

			public int maxTiles;

			public int voxelWalkableClimb;
			public uint voxelWalkableHeight;
			public float cellSize;
			public float cellHeight;
			public float maxSlope;
			public Matrix4x4 graphToWorldSpace;
			public int characterRadiusInVoxels;
			public int tileBorderSizeInVoxels;
			public int minRegionSize;
			public float maxEdgeLength;
			public float contourMaxError;
			[ReadOnly]
			public NativeArray<JobBuildRegions.RelevantGraphSurfaceInfo> relevantGraphSurfaces;
			public RelevantGraphSurfaceMode relevantGraphSurfaceMode;

			[NativeDisableUnsafePtrRestriction]
			public unsafe int* currentTileCounter;

			public bool allowBoundsChecks => false;

			public void SetOutputMeshes (NativeArray<TileBuilderBurst.TileMesh.TileMeshUnsafe> arr) {
				unsafe {
					outputMeshes = (TileBuilderBurst.TileMesh.TileMeshUnsafe*)arr.GetUnsafeReadOnlyPtr();
				}
			}

			public void SetCounter (NativeArray<int> arr) {
				unsafe {
					currentTileCounter = (int*)arr.GetUnsafePtr();
				}
			}

			public void Execute () {
				for (int k = 0; k < maxTiles; k++) {
#if UNITY_2020_1_OR_NEWER
					// Grab the next tile index that we should calculate
					int i;
					unsafe {
						i = System.Threading.Interlocked.Increment(ref UnsafeUtility.AsRef<int>(currentTileCounter)) - 1;
					}
					if (i >= tileGraphSpaceBounds.Length) return;
#else
					int i = -1;
					// if statement throws off the unreachable code warning
					if (k == 0) throw new System.Exception("Only supported in Unity 2020.1 or newer");
#endif

					var bucketStart = inputMeshes.bucketRanges[i];
					var bucketEnd = inputMeshes.bucketRanges[i+1];
					new JobVoxelize {
						inputMeshes = inputMeshes.meshes,
						bucket = inputMeshes.pointers.GetSubArray(bucketStart, bucketEnd - bucketStart),
						voxelWalkableClimb = voxelWalkableClimb,
						voxelWalkableHeight = voxelWalkableHeight,
						cellSize = cellSize,
						cellHeight = cellHeight,
						maxSlope = maxSlope,
						graphTransform = graphToWorldSpace,
						graphSpaceBounds = tileGraphSpaceBounds[i],
						voxelArea = tileBuilder.linkedVoxelField,
					}.Execute();

					new JobFilterLedges {
						field = tileBuilder.linkedVoxelField,
						voxelWalkableClimb = voxelWalkableClimb,
						voxelWalkableHeight = voxelWalkableHeight,
						cellSize = cellSize,
						cellHeight = cellHeight,
					}.Execute();

					new JobFilterLowHeightSpans {
						field = tileBuilder.linkedVoxelField,
						voxelWalkableHeight = voxelWalkableHeight,
					}.Execute();

					new JobBuildCompactField {
						input = tileBuilder.linkedVoxelField,
						output = tileBuilder.compactVoxelField,
					}.Execute();

					new JobBuildConnections {
						field = tileBuilder.compactVoxelField,
						voxelWalkableHeight = (int)voxelWalkableHeight,
						voxelWalkableClimb = voxelWalkableClimb,
					}.Execute();

					new JobErodeWalkableArea {
						field = tileBuilder.compactVoxelField,
						radius = characterRadiusInVoxels,
					}.Execute();

					new JobBuildDistanceField {
						field = tileBuilder.compactVoxelField,
						output = tileBuilder.distanceField,
					}.Execute();

					new JobBuildRegions {
						field = tileBuilder.compactVoxelField,
						distanceField = tileBuilder.distanceField,
						borderSize = tileBorderSizeInVoxels,
						minRegionSize = Mathf.RoundToInt(minRegionSize),
						srcQue = tileBuilder.tmpQueue1,
						dstQue = tileBuilder.tmpQueue2,
						relevantGraphSurfaces = relevantGraphSurfaces,
						relevantGraphSurfaceMode = relevantGraphSurfaceMode,
						cellSize = cellSize,
						cellHeight = cellHeight,
						graphTransform = graphToWorldSpace,
						graphSpaceBounds = tileGraphSpaceBounds[i],
					}.Execute();


					new JobBuildContours {
						field = tileBuilder.compactVoxelField,
						maxError = contourMaxError,
						maxEdgeLength = maxEdgeLength,
						buildFlags = Voxelize.RC_CONTOUR_TESS_WALL_EDGES | Voxelize.RC_CONTOUR_TESS_TILE_EDGES,
						cellSize = cellSize,
						outputContours = tileBuilder.contours,
						outputVerts = tileBuilder.contourVertices,
					}.Execute();

					new JobBuildMesh {
						contours = tileBuilder.contours,
						contourVertices = tileBuilder.contourVertices,
						mesh = tileBuilder.voxelMesh,
						field = tileBuilder.compactVoxelField,
					}.Execute();

					unsafe {
						TileBuilderBurst.TileMesh.TileMeshUnsafe* outputTileMesh = outputMeshes + i;
						*outputTileMesh = new TileBuilderBurst.TileMesh.TileMeshUnsafe {
							verticesInTileSpace = new UnsafeAppendBuffer(0, 4, Allocator.Persistent),
							triangles = new UnsafeAppendBuffer(0, 4, Allocator.Persistent),
							tags = new UnsafeAppendBuffer(0, 4, Allocator.Persistent),
						};

						new Pathfinding.Voxels.Utility.JobConvertAreasToTags {
							inputAreas = tileBuilder.voxelMesh.areas,
							outputTags = &outputTileMesh->tags,
						}.Execute();

						new MeshUtility.JobRemoveDuplicateVertices {
							vertices = tileBuilder.voxelMesh.verts.AsArray(),
							triangles = tileBuilder.voxelMesh.tris.AsArray(),
							outputVertices = &outputTileMesh->verticesInTileSpace,
							outputTriangles = &outputTileMesh->triangles,
						}.Execute();

						new Pathfinding.Voxels.Utility.JobTransformTileCoordinates {
							vertices = &outputTileMesh->verticesInTileSpace,
							matrix = voxelToTileSpace,
						}.Execute();
					}
				}
			}
		}

		protected NavmeshTile BuildTileMesh (Voxelize vox, int x, int z, int threadIndex = 0) {
			AstarProfiler.StartProfile("Build Tile");
			AstarProfiler.StartProfile("Init");

			vox.borderSize = TileBorderSizeInVoxels;
			vox.forcedBounds = CalculateTileBoundsWithBorder(x, z);
			vox.width = tileSizeX + vox.borderSize*2;
			vox.depth = tileSizeZ + vox.borderSize*2;

			if (!useTiles && relevantGraphSurfaceMode == RelevantGraphSurfaceMode.OnlyForCompletelyInsideTile) {
				// This best reflects what the user would actually want
				vox.relevantGraphSurfaceMode = RelevantGraphSurfaceMode.RequireForAll;
			} else {
				vox.relevantGraphSurfaceMode = relevantGraphSurfaceMode;
			}

			vox.minRegionSize = Mathf.RoundToInt(minRegionSize / (cellSize*cellSize));

			AstarProfiler.EndProfile("Init");


			// Init voxelizer
			vox.Init();
			vox.VoxelizeInput(transform, CalculateTileBoundsWithBorder(x, z));

			AstarProfiler.StartProfile("Filter Ledges");


			vox.FilterLedges(vox.voxelWalkableHeight, vox.voxelWalkableClimb, vox.cellSize, vox.cellHeight);

			AstarProfiler.EndProfile("Filter Ledges");

			AstarProfiler.StartProfile("Filter Low Height Spans");
			vox.FilterLowHeightSpans(vox.voxelWalkableHeight, vox.cellSize, vox.cellHeight);
			AstarProfiler.EndProfile("Filter Low Height Spans");

			vox.BuildCompactField();
			vox.BuildVoxelConnections();
			vox.ErodeWalkableArea(CharacterRadiusInVoxels);
			vox.BuildDistanceField();
			vox.BuildRegions();

			var cset = new Voxels.VoxelContourSet();
			vox.BuildContours(contourMaxError, 1, cset, Voxelize.RC_CONTOUR_TESS_WALL_EDGES | Voxelize.RC_CONTOUR_TESS_TILE_EDGES);

			Voxels.VoxelMesh mesh;
			vox.BuildPolyMesh(cset, 3, out mesh);

			AstarProfiler.StartProfile("Build Nodes");

			// Position the vertices correctly in graph space (all tiles are laid out on the xz plane with the (0,0) tile at the origin)
			for (int i = 0; i < mesh.verts.Length; i++) {
				mesh.verts[i] *= Int3.Precision;
			}
			vox.transformVoxel2Graph.Transform(mesh.verts);

			NavmeshTile tile = CreateTile(mesh, x, z, threadIndex);

			AstarProfiler.EndProfile("Build Nodes");

			AstarProfiler.EndProfile("Build Tile");
			return tile;
		}

		/// <summary>
		/// Create a tile at tile index x, z from the mesh.
		/// Version: Since version 3.7.6 the implementation is thread safe
		/// </summary>
		NavmeshTile CreateTile (Voxels.VoxelMesh mesh, int x, int z, int threadIndex) {
			if (mesh.tris == null) throw new System.ArgumentNullException("mesh.tris");
			if (mesh.verts == null) throw new System.ArgumentNullException("mesh.verts");
			if (mesh.tris.Length % 3 != 0) throw new System.ArgumentException("Indices array's length must be a multiple of 3 (mesh.tris)");
			if (mesh.verts.Length >= VertexIndexMask) {
				if (tileXCount*tileZCount == 1) {
					throw new System.ArgumentException("Too many vertices per tile (more than " + VertexIndexMask + ")." +
						"\n<b>Try enabling tiling in the recast graph settings.</b>\n");
				} else {
					throw new System.ArgumentException("Too many vertices per tile (more than " + VertexIndexMask + ")." +
						"\n<b>Try reducing tile size or enabling ASTAR_RECAST_LARGER_TILES under the 'Optimizations' tab in the A* Inspector</b>");
				}
			}

			// Create a new navmesh tile and assign its settings
			var tile = new NavmeshTile {
				x = x,
				z = z,
				w = 1,
				d = 1,
				tris = mesh.tris,
				bbTree = new BBTree(),
				graph = this,
			};

			tile.vertsInGraphSpace = Utility.RemoveDuplicateVertices(mesh.verts, tile.tris);
			tile.verts = (Int3[])tile.vertsInGraphSpace.Clone();
			transform.Transform(tile.verts);

			// We need to lock here because creating nodes is not thread safe
			// and we may be doing this from multiple threads at the same time
			tile.nodes = new TriangleMeshNode[tile.tris.Length/3];
			lock (active) {
				CreateNodes(tile, tile.tris, x + z*tileXCount, graphIndex, mesh.tags, active, initialPenalty, RecalculateNormals);
			}

			tile.bbTree.RebuildFrom(tile);
			CreateNodeConnections(tile.nodes);

			return tile;
		}

		/// <summary>
		/// Resize the number of tiles that this graph contains.
		///
		/// This can be used both to make a graph larger, smaller or move the bounds of the graph around.
		/// The new bounds are relative to the existing bounds which are IntRect(0, 0, tileCountX-1, tileCountZ-1).
		///
		/// Any current tiles that fall outside the new bounds will be removed.
		/// Any new tiles that did not exist inside the previous bounds will be created as empty tiles.
		/// All other tiles will be preserved. They will stay at their current world space positions.
		///
		/// Note: This is intended to be used at runtime on an already scanned graph.
		/// If you want to change the bounding box of a graph like in the editor, use <see cref="forcedBoundsSize"/> and <see cref="forcedBoundsCenter"/> instead.
		///
		/// <code>
		/// AstarPath.active.AddWorkItem(() => {
		///     var graph = AstarPath.active.data.recastGraph;
		///     var currentBounds = new IntRect(0, 0, graph.tileXCount-1, graph.tileZCount-1);
		///
		///     // Make the graph twice as large, but discard the first 3 columns.
		///     // All other tiles will be kept and stay at the same position in the world.
		///     // The new tiles will be empty.
		///     graph.Resize(new IntRect(3, 0, currentBounds.xmax*2, currentBounds.ymax*2));
		/// });
		/// </code>
		/// </summary>
		/// <param name="newTileBounds">Rectangle of tiles that the graph should contain. Relative to the old bounds.</param>
		public virtual void Resize (IntRect newTileBounds) {
			AssertSafeToUpdateGraph();

			if (!newTileBounds.IsValid()) throw new System.ArgumentException("Invalid tile bounds");
			if (newTileBounds == new IntRect(0, 0, tileXCount-1, tileZCount-1)) return;
			if (newTileBounds.Area == 0) throw new System.ArgumentException("Tile count must at least 1x1");

			StartBatchTileUpdate();
			// Update the graph's bounding box so that it covers the new tiles
			this.forcedBoundsSize = new Vector3(newTileBounds.Width*TileWorldSizeX, forcedBoundsSize.y, newTileBounds.Height*TileWorldSizeZ);
			this.forcedBoundsCenter = this.transform.Transform(
				new Vector3(
					(newTileBounds.xmin + newTileBounds.xmax + 1)*0.5f*TileWorldSizeX,
					forcedBoundsSize.y*0.5f,
					(newTileBounds.ymin + newTileBounds.ymax + 1)*0.5f*TileWorldSizeZ
					)
				);
			this.transform = CalculateTransform();
			var offset = -(Int3)(new Vector3(TileWorldSizeX * newTileBounds.xmin, 0, TileWorldSizeZ * newTileBounds.ymin));

			var newTiles = new NavmeshTile[newTileBounds.Area];
			for (int z = 0; z < tileZCount; z++) {
				for (int x = 0; x < tileXCount; x++) {
					Int2 tileCoordinates = new Int2(x, z);
					if (newTileBounds.Contains(x, z)) {
						NavmeshTile tile = tiles[x + z*tileXCount];
						newTiles[(x - newTileBounds.xmin) + (z - newTileBounds.ymin)*newTileBounds.Width] = tile;
					} else {
						ClearTile(x, z);
					}
				}
			}

			for (int z = 0; z < newTileBounds.Height; z++) {
				for (int x = 0; x < newTileBounds.Width; x++) {
					var tileIndex = x + z*newTileBounds.Width;
					var tile = newTiles[tileIndex];
					if (tile == null) {
						newTiles[tileIndex] = NewEmptyTile(x, z);
					} else {
						tile.x = x;
						tile.z = z;

						for (int i = 0; i < tile.nodes.Length; i++) {
							var node = tile.nodes[i];
							// The tile indices change when we resize the graph
							node.v0 = (node.v0 & VertexIndexMask) | (tileIndex << TileIndexOffset);
							node.v1 = (node.v1 & VertexIndexMask) | (tileIndex << TileIndexOffset);
							node.v2 = (node.v2 & VertexIndexMask) | (tileIndex << TileIndexOffset);
						}

						for (int i = 0; i < tile.vertsInGraphSpace.Length; i++) {
							tile.vertsInGraphSpace[i] += offset;
						}

						tile.vertsInGraphSpace.CopyTo(tile.verts, 0);
						transform.Transform(tile.verts);
					}
				}
			}
			this.tiles = newTiles;
			this.tileXCount = newTileBounds.Width;
			this.tileZCount = newTileBounds.Height;
			EndBatchTileUpdate();
			this.navmeshUpdateData.OnResized(newTileBounds);
		}

		/// <summary>Initialize the graph with empty tiles if it is not currently scanned</summary>
		public void EnsureInitialized () {
			AssertSafeToUpdateGraph();
			if (this.tiles == null) {
				TriangleMeshNode.SetNavmeshHolder(AstarPath.active.data.GetGraphIndex(this), this);
				transform = CalculateTransform();
				InitializeTileInfo();
				FillWithEmptyTiles();
			}
		}

		/// <summary>
		/// Load tiles from a <see cref="TileMeshes"/> object into this graph.
		///
		/// This can be used for many things, for example world streaming or placing large prefabs that have been pre-scanned.
		///
		/// The loaded tiles must have the same world-space size as this graph's tiles.
		/// The world-space size for a recast graph is given by the <see cref="cellSize"/> multiplied by <see cref="tileSizeX"/> (or <see cref="tileSizeZ)"/>.
		///
		/// If the graph is not scanned when this method is called, the graph will be initialized and consist of just the tiles loaded by this call.
		///
		/// <code>
		/// // Scans the first 6x6 chunk of tiles of the recast graph (the IntRect uses inclusive coordinates)
		/// var graph = AstarPath.active.data.recastGraph;
		/// var buildSettings = RecastGraph.RecastBuilder.BuildTileMeshes(graph, new IntRect(0, 0, 5, 5));
		/// var disposeArena = new Pathfinding.Jobs.DisposeArena();
		/// var promise = buildSettings.Schedule(disposeArena);
		///
		/// AstarPath.active.AddWorkItem(() => {
		///     // Wait for the asynchronous job to complete
		///     var result = promise.Complete();
		///     RecastGraph.RecastBuilder.TileMeshes tiles = result.tileMeshes.ToManaged();
		///     // Take the scanned tiles and place them in the graph,
		///     // but not at their original location, but 2 tiles away, rotated 90 degrees.
		///     tiles.tileRect = tiles.tileRect.Offset(new Int2(2, 0));
		///     tiles.Rotate(1);
		///     graph.ReplaceTiles(tiles);
		///
		///     // Dispose unmanaged data
		///     disposeArena.DisposeAll();
		///     result.Dispose();
		/// });
		/// </code>
		///
		/// Warning: All arrays in the tileMeshes parameter will be consumed by this method. You should not use them for anything else after calling this method.
		///
		/// See: <see cref="NavmeshPrefab"/>
		/// See: <see cref="TileMeshes"/>
		/// See: <see cref="RecastBuilder.BuildTileMeshes"/>
		/// See: <see cref="ReplaceTile"/>
		/// See: <see cref="TileWorldSizeX"/>
		/// See: <see cref="TileWorldSizeZ"/>
		/// </summary>
		/// <param name="tileMeshes">The tiles to load. They will be loaded into the graph at the \reflink{TileMeshes.tileRect} tile coordinates.</param>
		/// <param name="yOffset">All vertices in the loaded tiles will be moved upwards (or downwards if negative) by this amount.</param>
		public void ReplaceTiles (RecastBuilder.TileMeshes tileMeshes, float yOffset = 0) {
			AssertSafeToUpdateGraph();
			EnsureInitialized();

			if (tileMeshes.tileWorldSize.x != TileWorldSizeX || tileMeshes.tileWorldSize.y != TileWorldSizeZ) {
				throw new System.Exception("Loaded tile size does not match this graph's tile size.\n"
					+ "The source tiles have a world-space tile size of " + tileMeshes.tileWorldSize + " while this graph's tile size is (" + TileWorldSizeX + "," + TileWorldSizeZ + ").\n"
					+ "For a recast graph, the world-space tile size is defined as the cell size * the tile size in voxels");
			}

			var w = tileMeshes.tileRect.Width;
			var h = tileMeshes.tileRect.Height;
			UnityEngine.Assertions.Assert.AreEqual(w*h, tileMeshes.tileMeshes.Length);

			// Ensure the graph is large enough
			var newTileBounds = IntRect.Union(
				new IntRect(0, 0, tileXCount - 1, tileZCount - 1),
				tileMeshes.tileRect
				);
			Resize(newTileBounds);

			StartBatchTileUpdate();
			var updatedTiles = new NavmeshTile[w*h];
			for (int z = 0; z < h; z++) {
				for (int x = 0; x < w; x++) {
					var tile = tileMeshes.tileMeshes[x + z*w];

					var offset = (Int3) new Vector3(0, yOffset, 0);
					for (int i = 0; i < tile.verticesInTileSpace.Length; i++) {
						tile.verticesInTileSpace[i] += offset;
					}
					var tileCoordinates = new Int2(x, z) + tileMeshes.tileRect.Min - newTileBounds.Min;
					ReplaceTile(tileCoordinates.x, tileCoordinates.y, tile.verticesInTileSpace, tile.triangles);
					updatedTiles[x + z*w] = GetTile(tileCoordinates.x, tileCoordinates.y);
				}
			}
			EndBatchTileUpdate();

			navmeshUpdateData.OnRecalculatedTiles(updatedTiles);
			if (OnRecalculatedTiles != null) OnRecalculatedTiles(updatedTiles);
		}

		protected override void DeserializeSettingsCompatibility (GraphSerializationContext ctx) {
			base.DeserializeSettingsCompatibility(ctx);

			characterRadius = ctx.reader.ReadSingle();
			contourMaxError = ctx.reader.ReadSingle();
			cellSize = ctx.reader.ReadSingle();
			ctx.reader.ReadSingle(); // Backwards compatibility, cellHeight was previously read here
			walkableHeight = ctx.reader.ReadSingle();
			maxSlope = ctx.reader.ReadSingle();
			maxEdgeLength = ctx.reader.ReadSingle();
			editorTileSize = ctx.reader.ReadInt32();
			tileSizeX = ctx.reader.ReadInt32();
			nearestSearchOnlyXZ = ctx.reader.ReadBoolean();
			useTiles = ctx.reader.ReadBoolean();
			relevantGraphSurfaceMode = (RelevantGraphSurfaceMode)ctx.reader.ReadInt32();
			rasterizeColliders = ctx.reader.ReadBoolean();
			rasterizeMeshes = ctx.reader.ReadBoolean();
			rasterizeTerrain = ctx.reader.ReadBoolean();
			rasterizeTrees = ctx.reader.ReadBoolean();
			colliderRasterizeDetail = ctx.reader.ReadSingle();
			forcedBoundsCenter = ctx.DeserializeVector3();
			forcedBoundsSize = ctx.DeserializeVector3();
			mask = ctx.reader.ReadInt32();

			int count = ctx.reader.ReadInt32();
			tagMask = new List<string>(count);
			for (int i = 0; i < count; i++) {
				tagMask.Add(ctx.reader.ReadString());
			}

			showMeshOutline = ctx.reader.ReadBoolean();
			showNodeConnections = ctx.reader.ReadBoolean();
			terrainSampleSize = ctx.reader.ReadInt32();

			// These were originally forgotten but added in an upgrade
			// To keep backwards compatibility, they are only deserialized
			// If they exist in the streamed data
			walkableClimb = ctx.DeserializeFloat(walkableClimb);
			minRegionSize = ctx.DeserializeFloat(minRegionSize);

			// Make the world square if this value is not in the stream
			tileSizeZ = ctx.DeserializeInt(tileSizeX);

			showMeshSurface = ctx.reader.ReadBoolean();
		}
	}
}
