// NEXIM — Scene graph backed by TinyEmbree BVH acceleration structure.

using System.Numerics;
using TinyEmbree;
using NEXIM.Core.Models;

namespace NEXIM.Core.Rendering;

/// <summary>
/// Manages a collection of <see cref="SceneObject"/>s and exposes ray tracing
/// operations via Intel Embree (through the TinyEmbree wrapper).
///
/// <para>Lifecycle:</para>
/// <list type="number">
///   <item>Create a <see cref="SceneManager"/>.</item>
///   <item>Call <see cref="AddObject"/> for each mesh.</item>
///   <item>Call <see cref="Build"/> once — builds the BVH. No further adds allowed.</item>
///   <item>Use <see cref="Trace"/> and <see cref="IsOccluded"/> to query the scene.</item>
///   <item>Dispose when done.</item>
/// </list>
/// </summary>
public sealed class SceneManager : IDisposable
{
    readonly Raytracer _rt = new();
    readonly List<SceneObject> _objects = new();
    readonly Dictionary<TriangleMesh, SceneObject> _meshToObject = new(ReferenceEqualityComparer.Instance);
    bool _committed;
    int  _nextId;

    /// <summary>All scene objects, in insertion order.</summary>
    public IReadOnlyList<SceneObject> Objects => _objects;

    /// <summary>
    /// Adds a triangle mesh with the given material to the scene.
    /// Must be called before <see cref="Build"/>.
    /// </summary>
    /// <param name="vertices">
    /// World-space vertex positions. Winding order must be counter-clockwise
    /// when viewed from the outside (same convention as Embree).
    /// </param>
    /// <param name="indices">
    /// Triangle indices, three per face. Must be a multiple of three.
    /// </param>
    /// <param name="material">Surface material for this mesh.</param>
    /// <returns>Unique integer ID of the newly created <see cref="SceneObject"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when called after <see cref="Build"/>.
    /// </exception>
    public int AddObject(Vector3[] vertices, int[] indices, Material material)
    {
        if (_committed)
            throw new InvalidOperationException("Scene already committed — cannot add more objects.");

        var mesh = new TriangleMesh(vertices, indices);
        int id   = _nextId++;
        var obj  = new SceneObject(id, mesh, material);
        _objects.Add(obj);
        _meshToObject[mesh] = obj;
        _rt.AddMesh(mesh);
        return id;
    }

    /// <summary>
    /// Finalises the scene by building the Embree BVH acceleration structure.
    /// Must be called exactly once before any calls to <see cref="Trace"/> or
    /// <see cref="IsOccluded"/>.
    /// </summary>
    public void Build()
    {
        _rt.CommitScene();
        _committed = true;
    }

    /// <summary>
    /// Traces a ray against the scene and returns the closest hit and the
    /// associated <see cref="SceneObject"/> (or <c>null</c> if the ray misses
    /// all geometry).
    /// </summary>
    public (Hit hit, SceneObject? obj) Trace(Ray ray)
    {
        Hit hit = _rt.Trace(ray);
        if (!hit) return (hit, null);
        _meshToObject.TryGetValue(hit.Mesh, out SceneObject? obj);
        return (hit, obj);
    }

    /// <summary>
    /// Returns <c>true</c> if a shadow ray cast from <paramref name="from"/> in
    /// <paramref name="direction"/> is blocked by any scene geometry (i.e., the
    /// surface point is in shadow for a light in that direction).
    /// Uses a large but finite target distance (1 × 10⁶ world units) to avoid
    /// floating-point issues with infinite shadow rays.
    /// </summary>
    public bool IsOccluded(Hit from, Vector3 direction)
    {
        Vector3 dir    = Vector3.Normalize(direction);
        Vector3 target = from.Position + dir * 1_000_000.0f;
        return _rt.IsOccluded(from, target);
    }

    /// <inheritdoc/>
    public void Dispose() => _rt.Dispose();
}
