// NEXIM — Scene object: triangle mesh + material binding.

using TinyEmbree;
using NEXIM.Core.Models;

namespace NEXIM.Core.Rendering;

/// <summary>
/// A single renderable object: a triangle mesh with an associated surface material.
/// Instances are created by <see cref="SceneManager.AddObject"/> and are immutable.
/// </summary>
public sealed class SceneObject
{
    /// <summary>Unique identifier assigned by <see cref="SceneManager"/>.</summary>
    public int Id { get; }

    /// <summary>Triangle mesh passed to Embree for BVH intersection.</summary>
    public TriangleMesh Mesh { get; }

    /// <summary>Surface material that determines the BRDF applied at hit points.</summary>
    public Material Material { get; }

    internal SceneObject(int id, TriangleMesh mesh, Material material)
    {
        Id       = id;
        Mesh     = mesh;
        Material = material;
    }
}
