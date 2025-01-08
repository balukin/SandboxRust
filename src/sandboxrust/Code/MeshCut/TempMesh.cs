using System;
using System.Collections.Generic;


public class TempMesh
{
    public List<Vertex> vertices;
    public List<int> triangles;

    // Mappings of indices from original mesh to new mesh
    private Dictionary<int, int> vMapping;

    public float surfacearea;

    public Material material;

    public TempMesh(int vertexCapacity)
    {
        vertices = new List<Vertex>(vertexCapacity);
        triangles = new List<int>(vertexCapacity * 3);

        vMapping = new Dictionary<int, int>(vertexCapacity);

        surfacearea = 0;
    }

    public void Clear()
    {
        vertices.Clear();
        triangles.Clear();

        vMapping.Clear();

        surfacearea = 0;
    }

    /// <summary>
    /// Add point and normal to arrays if not already present
    /// </summary>
    private void AddPoint(Vertex v)
    {
        triangles.Add(vertices.Count);
        vertices.Add(v);
    }

    /// <summary>
    /// Add triangles from the original mesh. Therefore, no new vertices to add 
    /// and no normals to compute
    /// </summary>
    public void AddOgTriangle(int[] indices)
    {
        for (int i = 0; i < 3; ++i)
            triangles.Add(vMapping[indices[i]]);

        //Compute triangle area
        surfacearea += GetTriangleArea(triangles.Count - 3);
    }

    public void AddSlicedTriangle(int i1, Vector3 v2, Vector2 uv2, int i3)
    {
        int v1 = vMapping[i1],
            v3 = vMapping[i3];
        Vector3 normal = Vector3.Cross(v2 - vertices[v1].Position, vertices[v3].Position - v2).Normal;

        triangles.Add(v1);
        AddPoint(new Vertex { Position = v2, Normal = normal, TexCoord0 = uv2 });
        triangles.Add(vMapping[i3]);

        //Compute triangle area
        surfacearea += GetTriangleArea(triangles.Count - 3);
    }

    public void AddSlicedTriangle(int i1, Vector3 v2, Vector3 v3, Vector2 uv2, Vector2 uv3)
    {
        // Compute face normal?
        int v1 = vMapping[i1];
        Vector3 normal = Vector3.Cross(v2 - vertices[v1].Position, v3 - v2).Normal;

        triangles.Add(v1);
        AddPoint(new Vertex { Position = v2, Normal = normal, TexCoord0 = uv2 });
        AddPoint(new Vertex { Position = v3, Normal = normal, TexCoord0 = uv3 });

        //Compute triangle area
        surfacearea += GetTriangleArea(triangles.Count - 3);
    }

    /// <summary>
    /// Add a completely new triangle to the mesh
    /// </summary>
    public void AddTriangle(Vector3[] points)
    {
        // Compute normal
        Vector3 normal = Vector3.Cross(points[1] - points[0], points[2] - points[1]).Normal;

        for (int i = 0; i < 3; ++i)
        {
            // TODO: Compute uv values for the new triangle?
            AddPoint(new Vertex { Position = points[i], Normal = normal, TexCoord0 = Vector2.Zero });
        }

        //Compute triangle area
        surfacearea += GetTriangleArea(triangles.Count - 3);
    }

    public void ContainsKeys(List<ushort> triangles, int startIdx, bool[] isTrue)
    {
        for (int i = 0; i < 3; ++i)
            isTrue[i] = vMapping.ContainsKey(triangles[startIdx + i]);
    }

    /// <summary>
    /// Add a vertex from the original mesh 
    /// while storing its old index in the dictionary of index mappings
    /// </summary>
    public void AddVertex(List<Vertex> ogVertices, int index)
    {
        vMapping[index] = vertices.Count;
        vertices.Add(ogVertices[index]);

    }


    private float GetTriangleArea(int i)
    {
        var va = vertices[triangles[i + 2]].Position - vertices[triangles[i]].Position;
        var vb = vertices[triangles[i + 1]].Position - vertices[triangles[i]].Position;
        float a = va.Length;
        float b = vb.Length;
        float gamma = Deg2Rad(Vector3.GetAngle(vb, va));

        return a * b * MathF.Sin(gamma) / 2;
    }

    private float Deg2Rad(float degrees)
    {
        return MathF.PI * degrees / 180;
    }
}

