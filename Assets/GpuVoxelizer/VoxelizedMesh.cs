#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.Profiling;

[ExecuteAlways]
public class VoxelizedMesh : MonoBehaviour
{
    [SerializeField] MeshFilter _meshFilter;
    [SerializeField] MeshCollider _meshCollider;
    [SerializeField] float _halfSize = 0.05f;
    [SerializeField] Vector3 _boundsMin;

    [SerializeField] Material _gridPointMaterial;
    [SerializeField] int _gridPointCount;

    [SerializeField] Material _blocksMaterial;

    [SerializeField] ComputeShader _voxelizeComputeShader;
    ComputeBuffer _voxelPointsBuffer;
    ComputeBuffer _meshVerticesBuffer;
    ComputeBuffer _meshTrianglesBuffer;

    ComputeBuffer _pointsArgsBuffer;
    ComputeBuffer _blocksArgsBuffer;

    Mesh _voxelMesh;

    [SerializeField] bool _drawPointGrid;
    [SerializeField] bool _drawBlocks;

    static readonly int LocalToWorldMatrix = Shader.PropertyToID("_LocalToWorldMatrix");
    static readonly int BoundsMin = Shader.PropertyToID("_BoundsMin");
    static readonly int VoxelGridPoints = Shader.PropertyToID("_VoxelGridPoints");

    Vector4[] _gridPoints;

    void OnEnable()
    {
        _pointsArgsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        _blocksArgsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
    }

    void OnDisable()
    {
        _pointsArgsBuffer?.Dispose();
        _blocksArgsBuffer?.Dispose();

        _voxelPointsBuffer?.Dispose();

        _meshTrianglesBuffer?.Dispose();
        _meshVerticesBuffer?.Dispose();
    }

    void Update()
    {
        VoxelizeMeshWithGPU();

        if (_drawPointGrid)
        {
            _gridPointMaterial.SetMatrix(LocalToWorldMatrix, transform.localToWorldMatrix);
            _gridPointMaterial.SetVector(BoundsMin, new Vector4(_boundsMin.x, _boundsMin.y, _boundsMin.z, 0.0f));
            _gridPointMaterial.SetBuffer(VoxelGridPoints, _voxelPointsBuffer);
            _pointsArgsBuffer.SetData(new[] {1, _gridPointCount, 0, 0, 0});
            Graphics.DrawProceduralIndirect(_gridPointMaterial, _meshCollider.bounds, MeshTopology.Points,
                _pointsArgsBuffer);
        }

        if (_drawBlocks)
        {
            _blocksArgsBuffer.SetData(new[] {_voxelMesh.triangles.Length, _gridPointCount, 0, 0, 0});
            _blocksMaterial.SetBuffer("_Positions", _voxelPointsBuffer);
            Graphics.DrawMeshInstancedIndirect(_voxelMesh, 0, _blocksMaterial, _meshCollider.bounds, _blocksArgsBuffer);
        }
    }

    void VoxelizeMeshWithGPU()
    {
        Profiler.BeginSample("Voxelize Mesh (GPU)");

        Bounds bounds = _meshCollider.bounds;
        _boundsMin = transform.InverseTransformPoint(bounds.min);

        Vector3 voxelCount = bounds.extents / _halfSize;
        int xGridSize = Mathf.CeilToInt(voxelCount.x);
        int yGridSize = Mathf.CeilToInt(voxelCount.y);
        int zGridSize = Mathf.CeilToInt(voxelCount.z);

        bool resizeVoxelPointsBuffer = false;
        if (_gridPoints == null || _gridPoints.Length != xGridSize * yGridSize * zGridSize ||
            _voxelPointsBuffer == null)
        {
            _gridPoints = new Vector4[xGridSize * yGridSize * zGridSize];
            resizeVoxelPointsBuffer = true;
        }

        if (resizeVoxelPointsBuffer || _voxelPointsBuffer == null || !_voxelPointsBuffer.IsValid())
        {
            _voxelPointsBuffer?.Dispose();
            _voxelPointsBuffer = new ComputeBuffer(xGridSize * yGridSize * zGridSize, 4 * sizeof(float));
        }


        if (resizeVoxelPointsBuffer)
        {
            _voxelPointsBuffer.SetData(_gridPoints);

            _voxelMesh = GenerateVoxelMesh(_halfSize * 2.0f);
        }

        if (_meshVerticesBuffer == null || !_meshVerticesBuffer.IsValid())
        {
            _meshVerticesBuffer?.Dispose();

            var sharedMesh = _meshFilter.sharedMesh;
            _meshVerticesBuffer = new ComputeBuffer(sharedMesh.vertexCount, 3 * sizeof(float));
            _meshVerticesBuffer.SetData(sharedMesh.vertices);
        }

        if (_meshTrianglesBuffer == null || !_meshTrianglesBuffer.IsValid())
        {
            _meshTrianglesBuffer?.Dispose();

            var sharedMesh = _meshFilter.sharedMesh;
            _meshTrianglesBuffer = new ComputeBuffer(sharedMesh.triangles.Length, sizeof(int));
            _meshTrianglesBuffer.SetData(sharedMesh.triangles);
        }

        var voxelizeKernel = _voxelizeComputeShader.FindKernel("VoxelizeMesh");
        _voxelizeComputeShader.SetInt("_GridWidth", xGridSize);
        _voxelizeComputeShader.SetInt("_GridHeight", yGridSize);
        _voxelizeComputeShader.SetInt("_GridDepth", zGridSize);

        _voxelizeComputeShader.SetFloat("_CellHalfSize", _halfSize);

        _voxelizeComputeShader.SetBuffer(voxelizeKernel, VoxelGridPoints, _voxelPointsBuffer);
        _voxelizeComputeShader.SetBuffer(voxelizeKernel, "_MeshVertices", _meshVerticesBuffer);
        _voxelizeComputeShader.SetBuffer(voxelizeKernel, "_MeshTriangleIndices", _meshTrianglesBuffer);
        _voxelizeComputeShader.SetInt("_TriangleCount", _meshFilter.sharedMesh.triangles.Length);

        _voxelizeComputeShader.SetVector(BoundsMin, _boundsMin);

        _voxelizeComputeShader.GetKernelThreadGroupSizes(voxelizeKernel, out uint xGroupSize, out uint yGroupSize,
            out uint zGroupSize);

        _voxelizeComputeShader.Dispatch(voxelizeKernel,
            Mathf.CeilToInt(xGridSize / (float) xGroupSize),
            Mathf.CeilToInt(yGridSize / (float) yGroupSize),
            Mathf.CeilToInt(zGridSize / (float) zGroupSize));
        _gridPointCount = _voxelPointsBuffer.count;
        _voxelPointsBuffer.GetData(_gridPoints);

        Profiler.EndSample();
    }

    static Mesh GenerateVoxelMesh(float size)
    {
        var mesh = new Mesh();
        Vector3[] vertices =
        {
            //Front
            new Vector3(0, 0, 0),       // Front Bottom Left    0
            new Vector3(size, 0, 0),    // Front Bottom Right   1
            new Vector3(size, size, 0), // Front Top Right      2
            new Vector3(0, size, 0),    // Front Top Left       3

            //Top
            new Vector3(size, size, 0),     // Front Top Right      4
            new Vector3(0, size, 0),        // Front Top Left          5
            new Vector3(0, size, size),     // Back Top Left        6
            new Vector3(size, size, size),  // Back Top Right    7

            //Right
            new Vector3(size, 0, 0),        // Front Bottom Right      8
            new Vector3(size, size, 0),     // Front Top Right      9
            new Vector3(size, size, size),  // Back Top Right    10
            new Vector3(size, 0, size),     // Back Bottom Right    11

            //Left
            new Vector3(0, 0, 0),       // Front Bottom Left          12
            new Vector3(0, size, 0),    // Front Top Left          13
            new Vector3(0, size, size), // Back Top Left        14
            new Vector3(0, 0, size),    // Back Bottom Left        15

            //Back
            new Vector3(0, size, size),     // Back Top Left        16
            new Vector3(size, size, size),  // Back Top Right    17
            new Vector3(size, 0, size),     // Back Bottom Right    18
            new Vector3(0, 0, size),        // Back Bottom Left        19

            //Bottom
            new Vector3(0, 0, 0),       // Front Bottom Left          20
            new Vector3(size, 0, 0),    // Front Bottom Right      21
            new Vector3(size, 0, size), // Back Bottom Right    22
            new Vector3(0, 0, size)     // Back Bottom Left         23
        };

        int[] triangles =
        {
            //Front
            0, 2, 1,
            0, 3, 2,

            // Top
            4, 5, 6,
            4, 6, 7,

            // Right
            8, 9, 10,
            8, 10, 11,

            // Left
            12, 15, 14,
            12, 14, 13,

            // Back
            17, 16, 19,
            17, 19, 18,

            // Bottom
            20, 22, 23,
            20, 21, 22
        };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        return mesh;
    }

#if UNITY_EDITOR
    void Reset()
    {
        _meshFilter = GetComponent<MeshFilter>();
        if (TryGetComponent(out MeshCollider meshCollider))
        {
            _meshCollider = meshCollider;
        }
        else
        {
            _meshCollider = gameObject.AddComponent<MeshCollider>();
        }

        var basePath = "Assets/GpuVoxelizer/";
        _gridPointMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{basePath}Materials/GridPointMaterial.mat");
        _voxelizeComputeShader =
            AssetDatabase.LoadAssetAtPath<ComputeShader>($"{basePath}ComputeShaders/VoxelizeMesh.compute");
    }
#endif
}