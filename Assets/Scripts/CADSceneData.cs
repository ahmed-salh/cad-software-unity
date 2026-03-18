using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// JSON-serializable snapshot of the entire CAD scene.
/// Saved as a .cadproj file (plain JSON, human-readable).
/// </summary>
[Serializable]
public class CADSceneData
{
    public string version = "1.0";
    public string sceneName = "Untitled";
    public string savedAt;                        // ISO 8601 timestamp
    public List<CADObjectData> objects = new();
}

[Serializable]
public class CADObjectData
{
    public string name;
    public string primitiveType;   // matches CADPrimitive enum name

    // Transform
    public SerializedVector3 position;
    public SerializedVector3 rotation;   // Euler angles
    public SerializedVector3 scale;

    // For Custom / Extruded meshes — store every triangle vertex
    // so we can reconstruct the mesh on load.
    public bool hasMeshData;
    public SerializedVector3[] meshVertices;
    public int[] meshTriangles;
    public SerializedVector3[] meshNormals;
}

/// <summary>Unity's Vector3 is not [Serializable] for JsonUtility.</summary>
[Serializable]
public struct SerializedVector3
{
    public float x, y, z;

    public SerializedVector3(Vector3 v) { x = v.x; y = v.y; z = v.z; }
    public Vector3 ToVector3() => new(x, y, z);

    public static SerializedVector3 From(Vector3 v) => new(v);
}