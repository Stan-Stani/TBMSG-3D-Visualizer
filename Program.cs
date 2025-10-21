// High-performance block visualizer using OpenTK (OpenGL for C#)
// Install: dotnet add package OpenTK --version 4.8.2
// Install: dotnet add package Microsoft.Data.Sqlite

using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public class BlockVisualizer : GameWindow
{
    private SqliteConnection _connection;
    private int _shaderProgram;
    private int _vao, _vbo, _instanceVbo;
    private Vector3 _cameraPos;
    private Vector3 _cameraFront = -Vector3.UnitZ;
    private Vector3 _cameraUp = Vector3.UnitY;
    private float _pitch, _yaw = -90f;
    private Vector2 _lastMousePos;

    private List<BlockInstance> _blocks = new List<BlockInstance>();
    private int _renderDistance = 100;
    private float _blockSize = 1.0f;

    [StructLayout(LayoutKind.Sequential)]
    struct BlockInstance
    {
        public Vector3 Position;
        public Vector3 Color;
    }

    public BlockVisualizer() : base(
        GameWindowSettings.Default,
            new NativeWindowSettings()
            {
                ClientSize = new Vector2i(1920, 1080),
                Title = "High-Performance Block Visualizer",
                API = ContextAPI.OpenGL,
                APIVersion = new Version(3, 3),      // Lower version
                Profile = ContextProfile.Core,       // Core profile
                Flags = ContextFlags.ForwardCompatible,
            })
    {
    }

    protected override void OnLoad()
    {
        base.OnLoad();


        GL.ClearColor(0.1f, 0.1f, 0.15f, 1.0f);
        GL.Enable(EnableCap.DepthTest);
        GL.FrontFace(FrontFaceDirection.Ccw);
        GL.CullFace(CullFaceMode.Back);
        GL.Enable(EnableCap.CullFace);



        _cameraPos = new Vector3(0, 50, 50);
        CursorState = CursorState.Grabbed;

        SetupShaders();
        SetupGeometry();
        LoadDatabase();
        LoadBlocks();
    }

    private void SetupShaders()
    {
        string vertexShader = @"
#version 450 core
layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 instancePos;
layout (location = 2) in vec3 instanceColor;
layout (location = 3) in vec3 aNormal;

uniform mat4 projection;
uniform mat4 view;

out vec3 fragColor;
out vec3 fragNormal;
out vec3 fragPos;

void main()
{
    vec3 worldPos = aPos + instancePos;
    gl_Position = projection * view * vec4(worldPos, 1.0);
    fragColor = instanceColor;
    fragNormal = aNormal;
    fragPos = worldPos;
}";

        string fragmentShader = @"
#version 450 core
in vec3 fragColor;
in vec3 fragNormal;
in vec3 fragPos;
out vec4 FragColor;

void main()
{
    // Better lighting
    vec3 lightDir = normalize(vec3(0.5, 1.0, 0.3));
    vec3 normal = normalize(fragNormal);

    // Diffuse lighting
    float diff = max(dot(normal, lightDir), 0.0);

    // Ambient + diffuse
    vec3 ambient = 0.4 * fragColor;
    vec3 diffuse = 0.6 * diff * fragColor;

    FragColor = vec4(ambient + diffuse, 1.0);
}";

        int vs = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(vs, vertexShader);
        GL.CompileShader(vs);
        CheckShaderCompilation(vs);

        int fs = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(fs, fragmentShader);
        GL.CompileShader(fs);
        CheckShaderCompilation(fs);

        _shaderProgram = GL.CreateProgram();
        GL.AttachShader(_shaderProgram, vs);
        GL.AttachShader(_shaderProgram, fs);
        GL.LinkProgram(_shaderProgram);

        GL.DeleteShader(vs);
        GL.DeleteShader(fs);
    }

    private void SetupGeometry()
    {
        // Cube vertices with normals (position + normal per vertex)
        float[] vertices = {
            // Front face (z = +0.5f)
            -0.5f, -0.5f,  0.5f,   0.0f,  0.0f,  1.0f, // Bottom-left
             0.5f, -0.5f,  0.5f,   0.0f,  0.0f,  1.0f, // Bottom-right
             0.5f,  0.5f,  0.5f,   0.0f,  0.0f,  1.0f, // Top-right

             0.5f,  0.5f,  0.5f,   0.0f,  0.0f,  1.0f, // Top-right
            -0.5f,  0.5f,  0.5f,   0.0f,  0.0f,  1.0f, // Top-left
            -0.5f, -0.5f,  0.5f,   0.0f,  0.0f,  1.0f, // Bottom-left

            // Back face (z = -0.5f)
            -0.5f, -0.5f, -0.5f,   0.0f,  0.0f, -1.0f, // Bottom-left
            -0.5f,  0.5f, -0.5f,   0.0f,  0.0f, -1.0f, // Top-left
             0.5f,  0.5f, -0.5f,   0.0f,  0.0f, -1.0f, // Top-right

             0.5f,  0.5f, -0.5f,   0.0f,  0.0f, -1.0f, // Top-right
             0.5f, -0.5f, -0.5f,   0.0f,  0.0f, -1.0f, // Bottom-right
            -0.5f, -0.5f, -0.5f,   0.0f,  0.0f, -1.0f, // Bottom-left

            // Top face (y = +0.5f)
            -0.5f,  0.5f,  0.5f,   0.0f,  1.0f,  0.0f, // Front-left
             0.5f,  0.5f,  0.5f,   0.0f,  1.0f,  0.0f, // Front-right
             0.5f,  0.5f, -0.5f,   0.0f,  1.0f,  0.0f, // Back-right

             0.5f,  0.5f, -0.5f,   0.0f,  1.0f,  0.0f, // Back-right
            -0.5f,  0.5f, -0.5f,   0.0f,  1.0f,  0.0f, // Back-left
            -0.5f,  0.5f,  0.5f,   0.0f,  1.0f,  0.0f, // Front-left

            // Bottom face (y = -0.5f)
            -0.5f, -0.5f,  0.5f,   0.0f, -1.0f,  0.0f, // Front-left
            -0.5f, -0.5f, -0.5f,   0.0f, -1.0f,  0.0f, // Back-left
             0.5f, -0.5f, -0.5f,   0.0f, -1.0f,  0.0f, // Back-right

             0.5f, -0.5f, -0.5f,   0.0f, -1.0f,  0.0f, // Back-right
             0.5f, -0.5f,  0.5f,   0.0f, -1.0f,  0.0f, // Front-right
            -0.5f, -0.5f,  0.5f,   0.0f, -1.0f,  0.0f, // Front-left

            // Right face (x = +0.5f)
             0.5f, -0.5f,  0.5f,   1.0f,  0.0f,  0.0f, // Bottom-front
             0.5f, -0.5f, -0.5f,   1.0f,  0.0f,  0.0f, // Bottom-back
             0.5f,  0.5f, -0.5f,   1.0f,  0.0f,  0.0f, // Top-back

             0.5f,  0.5f, -0.5f,   1.0f,  0.0f,  0.0f, // Top-back
             0.5f,  0.5f,  0.5f,   1.0f,  0.0f,  0.0f, // Top-front
             0.5f, -0.5f,  0.5f,   1.0f,  0.0f,  0.0f, // Bottom-front

            // Left face (x = -0.5f)
            -0.5f, -0.5f,  0.5f,  -1.0f,  0.0f,  0.0f, // Bottom-front
            -0.5f,  0.5f,  0.5f,  -1.0f,  0.0f,  0.0f, // Top-front
            -0.5f,  0.5f, -0.5f,  -1.0f,  0.0f,  0.0f, // Top-back

            -0.5f,  0.5f, -0.5f,  -1.0f,  0.0f,  0.0f, // Top-back
            -0.5f, -0.5f, -0.5f,  -1.0f,  0.0f,  0.0f, // Bottom-back
            -0.5f, -0.5f,  0.5f,  -1.0f,  0.0f,  0.0f  // Bottom-front
        };




        _vao = GL.GenVertexArray();
        GL.BindVertexArray(_vao);

        _vbo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float),
                     vertices, BufferUsageHint.StaticDraw);

        // Position attribute (3 floats for position, 3 floats for normal = 6 floats stride)
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);

        // Normal attribute
        GL.VertexAttribPointer(3, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 3 * sizeof(float));
        GL.EnableVertexAttribArray(3);

        // Instance VBO (will be updated per frame)
        _instanceVbo = GL.GenBuffer();
    }

    private void LoadDatabase()
    {
        string dbPath = "block-mining-simulation-game/world-1.sqlite";
        string connectionString = $"Data Source={dbPath};Mode=ReadOnly";
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
    }

    private void LoadBlocks()
    {
        _blocks.Clear();

        int minX = (int)(_cameraPos.X / _blockSize) - _renderDistance;
        int maxX = (int)(_cameraPos.X / _blockSize) + _renderDistance;
        int minY = (int)(_cameraPos.Y / _blockSize) - _renderDistance;
        int maxY = (int)(_cameraPos.Y / _blockSize) + _renderDistance;
        int minZ = (int)(_cameraPos.Z / _blockSize) - _renderDistance;
        int maxZ = (int)(_cameraPos.Z / _blockSize) + _renderDistance;

        string sql = @"SELECT x0, x1, x2, data FROM block
                      WHERE data IS NOT NULL AND LENGTH(TRIM(data)) > 0
                      AND x0 BETWEEN @minX AND @maxX
                      AND x1 BETWEEN @minY AND @maxY
                      AND x2 BETWEEN @minZ AND @maxZ";

        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@minX", minX);
        command.Parameters.AddWithValue("@maxX", maxX);
        command.Parameters.AddWithValue("@minY", minY);
        command.Parameters.AddWithValue("@maxY", maxY);
        command.Parameters.AddWithValue("@minZ", minZ);
        command.Parameters.AddWithValue("@maxZ", maxZ);

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            int x = reader.GetInt32(0);
            int y = reader.GetInt32(1);
            int z = reader.GetInt32(2);
            string data = reader.GetString(3);
            Console.WriteLine($"data: {data}");

            Vector3 pos = new Vector3(x * _blockSize, y * _blockSize, z * _blockSize);
            float dist = (pos - _cameraPos).Length;

            if (dist <= _renderDistance * _blockSize)
            {
                _blocks.Add(new BlockInstance
                {
                    Position = pos,
                    Color = GetColorFromData(data)
                });
            }
        }

        // Upload to GPU
        if (_blocks.Count > 0)
        {
            GL.BindBuffer(BufferTarget.ArrayBuffer, _instanceVbo);
            GL.BufferData(BufferTarget.ArrayBuffer,
                         _blocks.Count * Marshal.SizeOf<BlockInstance>(),
                         _blocks.ToArray(), BufferUsageHint.DynamicDraw);

            // Position attribute
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false,
                                  Marshal.SizeOf<BlockInstance>(), 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribDivisor(1, 1);

            // Color attribute
            GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false,
                                  Marshal.SizeOf<BlockInstance>(),
                                  Marshal.OffsetOf<BlockInstance>("Color"));
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribDivisor(2, 1);
        }

        Console.WriteLine($"Loaded {_blocks.Count} blocks");
    }

    private Vector3 GetColorFromData(string data)
    {
        // Use your CAS color map from Godot
        var colorMap = new Dictionary<string, Vector3>
        {
            ["CAS:1317-65-3"] = new Vector3(0.7f, 0.7f, 0.7f),   // Rock/Lime - gray
            ["CAS:1309-37-1"] = new Vector3(0.6f, 0.25f, 0.1f),  // Iron(III) Oxide - rust/reddish brown
            ["CAS:1317-60-8"] = new Vector3(0.4f, 0.15f, 0.15f), // Hematite - dark red/brown
            ["CAS:7439-89-6"] = new Vector3(0.5f, 0.45f, 0.3f),  // Metallic Iron - silvery gray
            ["CAS:7440-50-8"] = new Vector3(0.72f, 0.45f, 0.2f), // Metallic Copper - copper/orange
            ["CAS:1318-16-7"] = new Vector3(0.8f, 0.7f, 0.6f),   // Bauxite - reddish/tan
            ["CAS:12168-52-4"] = new Vector3(0.2f, 0.2f, 0.2f),  // Ilmenite - black/dark gray
            ["CAS:1309-36-0"] = new Vector3(0.8f, 0.7f, 0.4f),   // Pyrite - brassy gold
            ["CAS:13463-67-7"] = new Vector3(0.95f, 0.95f, 0.95f), // Titanium Dioxide - white
            ["CAS:7440-32-6"] = new Vector3(0.7f, 0.7f, 0.75f),  // Metallic Titanium - silvery gray
            ["CAS:14808-60-7"] = new Vector3(0.9f, 0.85f, 0.8f), // Silicon Dioxide (quartz)
            ["CAS:1304-50-3"] = new Vector3(0.7f, 0.85f, 0.5f),  // Chrysoberyl - yellowish green
            ["CAS:7440-22-4"] = new Vector3(0.9f, 0.9f, 0.95f),  // Metallic Silver - bright silver
            ["CAS:12069-69-1"] = new Vector3(0.2f, 0.6f, 0.3f),  // Malachite - bright green
            ["CAS:12249-26-2"] = new Vector3(0.45f, 0.35f, 0.3f), // Taconite - dark brown/gray
            ["CAS:1310-14-1"] = new Vector3(0.6f, 0.5f, 0.2f),   // Goethite - yellow/brown
            ["CAS:1309-38-2"] = new Vector3(0.3f, 0.3f, 0.3f),   // Magnetite - black/dark gray
            ["GRIN:28551"] = new Vector3(0.55f, 0.35f, 0.2f),    // Wooden Block - brown
            ["BMSG:1"] = new Vector3(0.5f, 0.3f, 0.15f),         // Wooden Pick - dark brown
            ["BMSG:2"] = new Vector3(0.6f, 0.6f, 0.6f),          // Stone Pick - gray
            ["BMSG:3"] = new Vector3(0.55f, 0.5f, 0.45f),        // Iron Pick - metallic gray
        };

        if (colorMap.ContainsKey(data))
        {
            return colorMap[data];
        }

        // Hash-based coloring for unknown types (but with better colors)
        int hash = data.GetHashCode();
        Random r = new Random(hash);

        // Generate nicer saturated colors
        float hue = (float)r.NextDouble();
        float saturation = 0.6f + (float)r.NextDouble() * 0.4f;
        float value = 0.5f + (float)r.NextDouble() * 0.5f;

        // Convert HSV to RGB
        float c = value * saturation;
        float x = c * (1 - Math.Abs((hue * 6) % 2 - 1));
        float m = value - c;

        float r1 = 0, g1 = 0, b1 = 0;
        if (hue < 1f/6f) { r1 = c; g1 = x; }
        else if (hue < 2f/6f) { r1 = x; g1 = c; }
        else if (hue < 3f/6f) { g1 = c; b1 = x; }
        else if (hue < 4f/6f) { g1 = x; b1 = c; }
        else if (hue < 5f/6f) { r1 = x; b1 = c; }
        else { r1 = c; b1 = x; }

        return new Vector3(r1 + m, g1 + m, b1 + m);
    }

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        base.OnRenderFrame(args);

        Title = string.Format( "High-Performance Block Visualizer | Press R to Load Current Area | X={0:F1} Y={1:F1} Z={2:F1}", _cameraPos.X, _cameraPos.Y, _cameraPos.Z );

        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        GL.UseProgram(_shaderProgram);

        // Set matrices
        Matrix4 view = Matrix4.LookAt(_cameraPos, _cameraPos + _cameraFront, _cameraUp);
        Matrix4 projection = Matrix4.CreatePerspectiveFieldOfView(
            MathHelper.DegreesToRadians(75f), Size.X / (float)Size.Y, 0.1f, 1000f);

        int viewLoc = GL.GetUniformLocation(_shaderProgram, "view");
        int projLoc = GL.GetUniformLocation(_shaderProgram, "projection");
        GL.UniformMatrix4(viewLoc, false, ref view);
        GL.UniformMatrix4(projLoc, false, ref projection);

        // Draw all blocks with instancing (ULTRA FAST!)
        GL.BindVertexArray(_vao);
        GL.DrawArraysInstanced(PrimitiveType.Triangles, 0, 36, _blocks.Count);

        SwapBuffers();
    }

    protected override void OnUpdateFrame(FrameEventArgs args)
    {
        base.OnUpdateFrame(args);

        var input = KeyboardState;
        float speed = 10f * (float)args.Time;

        if (input.IsKeyDown(Keys.W)) _cameraPos += _cameraFront * speed;
        if (input.IsKeyDown(Keys.S)) _cameraPos -= _cameraFront * speed;
        if (input.IsKeyDown(Keys.A)) _cameraPos -= Vector3.Normalize(Vector3.Cross(_cameraFront, _cameraUp)) * speed;
        if (input.IsKeyDown(Keys.D)) _cameraPos += Vector3.Normalize(Vector3.Cross(_cameraFront, _cameraUp)) * speed;
        if (input.IsKeyDown(Keys.Space)) _cameraPos.Y += speed;
        if (input.IsKeyDown(Keys.LeftShift)) _cameraPos.Y -= speed;

        if (input.IsKeyDown(Keys.R))
        {
            LoadBlocks(); // Reload blocks at new position
        }

        if (input.IsKeyDown(Keys.Escape)) Close();
    }

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        base.OnMouseMove(e);

        const float sensitivity = 0.1f;
        float deltaX = e.X - _lastMousePos.X;
        float deltaY = e.Y - _lastMousePos.Y;
        _lastMousePos = new Vector2(e.X, e.Y);

        _yaw += deltaX * sensitivity;
        _pitch -= deltaY * sensitivity;
        _pitch = Math.Clamp(_pitch, -89f, 89f);

        _cameraFront.X = MathF.Cos(MathHelper.DegreesToRadians(_pitch)) *
                        MathF.Cos(MathHelper.DegreesToRadians(_yaw));
        _cameraFront.Y = MathF.Sin(MathHelper.DegreesToRadians(_pitch));
        _cameraFront.Z = MathF.Cos(MathHelper.DegreesToRadians(_pitch)) *
                        MathF.Sin(MathHelper.DegreesToRadians(_yaw));
        _cameraFront = Vector3.Normalize(_cameraFront);
    }

    private void CheckShaderCompilation(int shader)
    {
        GL.GetShader(shader, ShaderParameter.CompileStatus, out int success);
        if (success == 0)
        {
            string log = GL.GetShaderInfoLog(shader);
            throw new Exception($"Shader compilation failed: {log}");
        }
    }

    protected override void OnUnload()
    {
        base.OnUnload();
        _connection?.Close();
        GL.DeleteVertexArray(_vao);
        GL.DeleteBuffer(_vbo);
        GL.DeleteBuffer(_instanceVbo);
        GL.DeleteProgram(_shaderProgram);
    }
}

class Program
{
    static void Main()
    {
        using var window = new BlockVisualizer();
        window.Run();
    }
}
