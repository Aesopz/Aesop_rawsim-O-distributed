using RAWSimO.Core;
using RAWSimO.Core.Bots;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using HelixToolkit.Wpf;

namespace RAWSimO.GymServer
{
    /// <summary>
    /// Renders per-bot onboard camera images using WPF offscreen rendering on a dedicated STA thread.
    /// Colors match the RAWSim-O snapshot aesthetic: dark-navy background, gray floor, maroon pods,
    /// steel-blue bot cylinders. The rendering bot's own cylinder is hidden during capture so the
    /// camera is not looking from inside its own geometry.
    /// </summary>
    internal class BotCameraRenderer : IDisposable
    {
        private readonly GymCameraConfiguration _config;
        private readonly Instance _instance;

        private Thread? _staThread;
        private Dispatcher? _dispatcher;
        private Viewport3D? _viewport;
        private Window? _hiddenWindow;

        // Per-bot visuals for dynamic position updates
        private readonly List<(BotNormal Bot, ModelVisual3D Visual)> _botVisuals = new();

        // Dynamic pod visual — must be created on the STA thread (initialized in StartSTAThread)
        private ModelVisual3D _podDynamicVisual = null!;

        private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);
        private bool _disposed = false;

        // ── Palette from VisualizationConstants.cs (exact match) ───────────────────
        private static readonly Color BackgroundColor    = Color.FromRgb(25,  45,  90);  // dark navy sky
        private static readonly Color FloorColor         = Color.FromRgb(211, 211, 211); // LightGray — BrushTierVisual
        private static readonly Color FloorGridColor     = Color.FromRgb(180, 180, 180); // slightly darker grid
        private static readonly Color PodColor           = Color.FromRgb(100, 149, 237); // CornflowerBlue — BrushPodVisual
        private static readonly Color PodSpecColor       = Color.FromRgb(210, 225, 255);
        private static readonly Color InputStationColor  = Color.FromRgb(255, 255,   0); // Yellow  — BrushInputStationVisual
        private static readonly Color OutputStationColor = Color.FromRgb(205,  92,  92); // IndianRed — BrushOutputStationVisual

        // Bot state → color (from StateBrushes in VisualizationConstants.cs)
        private static readonly Dictionary<string, Color> BotStateColors = new()
        {
            { "",             Color.FromRgb(169, 169, 169) }, // DarkGray  — unknown/empty
            { "Move",         Color.FromRgb(86,  175, 54)  }, // Green     — moving
            { "Evade",        Color.FromRgb(255, 105, 180) }, // HotPink   — evading
            { "GetItems",     Color.FromRgb(255, 105, 180) }, // HotPink   — getting items
            { "PutItems",     Color.FromRgb(148, 0,   211) }, // DarkViolet — putting items
            { "PickupPod",    Color.FromRgb(139, 0,   0)   }, // DarkRed   — picking up pod
            { "SetdownPod",   Color.FromRgb(255, 255, 0)   }, // Yellow    — setting down pod
            { "Rest",         Color.FromRgb(0,   0,   139) }, // DarkBlue  — resting
            { "UseElevator",  Color.FromRgb(0,   128, 128) }, // Teal      — using elevator
            { "Debug",        Color.FromRgb(255, 0,   0)   }, // Red       — debug
        };

        public BotCameraRenderer(Instance instance, GymCameraConfiguration config)
        {
            _instance = instance;
            _config   = config;
            StartSTAThread();
        }

        private void StartSTAThread()
        {
            _staThread = new Thread(() =>
            {
                if (Application.Current == null)
                    new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

                _dispatcher = Dispatcher.CurrentDispatcher;
                _dispatcher.Invoke(() =>
                {
                    _viewport = new Viewport3D();
                    _viewport.Camera = new PerspectiveCamera
                    {
                        Position          = new Point3D(0, 0, 1),
                        LookDirection     = new Vector3D(1, 0, 0),
                        UpDirection       = new Vector3D(0, 0, 1),
                        FieldOfView       = _config.FieldOfViewDeg,
                        NearPlaneDistance = 0.05,
                        FarPlaneDistance  = 300,
                    };

                    AddLighting();
                    BuildStaticScene();
                    BuildBotVisuals();
                    // Create and add pod visual here — must live on the STA thread (same Dispatcher as Viewport3D)
                    _podDynamicVisual = new ModelVisual3D();
                    _viewport!.Children.Add(_podDynamicVisual);
                    _emptyGroup = new Model3DGroup(); // Must be created on STA thread

                    _hiddenWindow = new Window
                    {
                        Left          = -10000,
                        Top           = -10000,
                        Width         = _config.ImageWidth,
                        Height        = _config.ImageHeight,
                        WindowStyle   = WindowStyle.None,
                        ShowInTaskbar = false,
                        ResizeMode    = ResizeMode.NoResize,
                        Background    = new SolidColorBrush(Color.FromRgb(25, 45, 90)),
                        Content       = _viewport,
                    };
                    _hiddenWindow.Show();
                    _ready.Set();
                });

                Dispatcher.Run();
            });

            _staThread.SetApartmentState(ApartmentState.STA);
            _staThread.IsBackground = true;
            _staThread.Name = "GymBotCameraRenderer-STA";
            _staThread.Start();
            _ready.Wait(TimeSpan.FromSeconds(30));
        }

        private void AddLighting()
        {
            var lights = new Model3DGroup();
            lights.Children.Add(new AmbientLight(Color.FromRgb(75, 75, 80)));
            lights.Children.Add(new DirectionalLight(Color.FromRgb(210, 205, 195), new Vector3D(-0.4, -0.3, -1)));
            lights.Children.Add(new DirectionalLight(Color.FromRgb(70,  90,  120), new Vector3D(1,   -0.1, -0.3)));
            _viewport!.Children.Add(new ModelVisual3D { Content = lights });
        }

        private void BuildStaticScene()
        {
            var sceneGroup = new Model3DGroup();

            foreach (var tier in _instance.Compound.Tiers)
            {
                double cx = tier.RelativePositionX + tier.Length / 2.0;
                double cy = tier.RelativePositionY + tier.Width  / 2.0;
                double z  = tier.RelativePositionZ;

                // Floor slab
                var floorMb = new MeshBuilder(false, false);
                floorMb.AddBox(new Point3D(cx, cy, z - 0.04), tier.Length, tier.Width, 0.08);
                var floorMat = new DiffuseMaterial(new SolidColorBrush(FloorColor));
                sceneGroup.Children.Add(new GeometryModel3D(floorMb.ToMesh(), floorMat)
                    { BackMaterial = floorMat });

                // Grid lines (1-unit spacing)
                var gridMb = new MeshBuilder(false, false);
                double x0 = tier.RelativePositionX, y0 = tier.RelativePositionY;
                int nx = (int)Math.Round(tier.Length), ny = (int)Math.Round(tier.Width);
                double lh = 0.006, lw = 0.03;
                for (int ix = 0; ix <= nx; ix++)
                    gridMb.AddBox(new Point3D(x0 + ix, cy, z + lh), lw, tier.Width + lw, lh * 2);
                for (int iy = 0; iy <= ny; iy++)
                    gridMb.AddBox(new Point3D(cx, y0 + iy, z + lh), tier.Length + lw, lw, lh * 2);
                var gridMat = new DiffuseMaterial(new SolidColorBrush(FloorGridColor));
                sceneGroup.Children.Add(new GeometryModel3D(gridMb.ToMesh(), gridMat)
                    { BackMaterial = gridMat });
            }

            // Input stations — flat yellow slabs (matching SimulationVisualInputStation3D, height=0.05)
            const double stationH = 0.05;
            var iStationMb = new MeshBuilder(false, false);
            foreach (var st in _instance.InputStations)
            {
                double z  = st.Tier?.RelativePositionZ ?? 0.0;
                double r  = st.Radius;
                iStationMb.AddBox(new Point3D(st.X, st.Y, z + stationH / 2.0), r * 2, r * 2, stationH);
            }
            if (iStationMb.Positions.Count > 0)
            {
                var iMat = new DiffuseMaterial(new SolidColorBrush(InputStationColor));
                sceneGroup.Children.Add(new GeometryModel3D(iStationMb.ToMesh(), iMat) { BackMaterial = iMat });
            }

            // Output stations — flat IndianRed slabs (matching SimulationVisualOutputStation3D)
            var oStationMb = new MeshBuilder(false, false);
            foreach (var st in _instance.OutputStations)
            {
                double z  = st.Tier?.RelativePositionZ ?? 0.0;
                double r  = st.Radius;
                oStationMb.AddBox(new Point3D(st.X, st.Y, z + stationH / 2.0), r * 2, r * 2, stationH);
            }
            if (oStationMb.Positions.Count > 0)
            {
                var oMat = new DiffuseMaterial(new SolidColorBrush(OutputStationColor));
                sceneGroup.Children.Add(new GeometryModel3D(oStationMb.ToMesh(), oMat) { BackMaterial = oMat });
            }

            _viewport!.Children.Add(new ModelVisual3D { Content = sceneGroup });
        }

        private void BuildBotVisuals()
        {
            foreach (var bot in _instance.Bots)
            {
                var visual = new ModelVisual3D { Content = MakeBotModel((BotNormal)bot) };
                _viewport!.Children.Add(visual);
                _botVisuals.Add(((BotNormal)bot, visual));
            }
        }

        /// <summary>
        /// Builds a pod scene from current simulation state.  Called every frame so that pods
        /// carried by bots appear at the bot's live position, matching SimulationVisualPod3D.
        ///
        /// Pod geometry mirrors SimulationVisualPod3D exactly:
        ///   _height      = radius * 4
        ///   main body    : Height = radius*3,  center Z = tierZ + radius*2 + radius*0.5 = tierZ + radius*2.5
        ///   feet (×4)    : Height = radius,    center Z = tierZ + radius*0.5
        ///   foot XY size : radius / 10
        ///   foot corners : ±(radius - footSize/2) in X and Y
        /// </summary>
        private Model3DGroup BuildCurrentPodScene()
        {
            var group = new Model3DGroup();
            var bodyMb = new MeshBuilder(true, false);
            var footMb = new MeshBuilder(false, false);

            foreach (var pod in _instance.Pods)
            {
                double r  = pod.Radius;
                double z  = pod.Tier?.RelativePositionZ ?? 0.0;
                double px = pod.X;
                double py = pod.Y;

                // Main body — matches SimulationVisualPod3D main BoxVisual3D
                double bodyH   = r * 3.0;
                double bodyCZ  = z + r * 2.5;
                bodyMb.AddBox(new Point3D(px, py, bodyCZ), r * 2, r * 2, bodyH);

                // Four corner feet — matches SimulationVisualPod3D feet (DetailLevel.Aesthetics)
                double footL  = r / 10.0;
                double footH  = r;
                double footCZ = z + footH / 2.0;
                double offset = r - footL / 2.0;
                footMb.AddBox(new Point3D(px - offset, py - offset, footCZ), footL, footL, footH);
                footMb.AddBox(new Point3D(px + offset, py - offset, footCZ), footL, footL, footH);
                footMb.AddBox(new Point3D(px - offset, py + offset, footCZ), footL, footL, footH);
                footMb.AddBox(new Point3D(px + offset, py + offset, footCZ), footL, footL, footH);
            }

            if (bodyMb.Positions.Count > 0)
            {
                var podMat = new MaterialGroup();
                podMat.Children.Add(new DiffuseMaterial(new SolidColorBrush(PodColor)));
                podMat.Children.Add(new SpecularMaterial(new SolidColorBrush(PodSpecColor), 25));
                group.Children.Add(new GeometryModel3D(bodyMb.ToMesh(), podMat) { BackMaterial = podMat });
            }

            if (footMb.Positions.Count > 0)
            {
                var footMat = new DiffuseMaterial(new SolidColorBrush(PodColor));
                group.Children.Add(new GeometryModel3D(footMb.ToMesh(), footMat) { BackMaterial = footMat });
            }

            return group;
        }

        private GeometryModel3D MakeBotModel(BotNormal bot)
        {
            double z     = bot.Tier?.RelativePositionZ ?? 0.0;
            string state = bot.GetInfoState() ?? "";
            Color  col   = BotStateColors.TryGetValue(state, out var c)
                           ? c : BotStateColors[""];

            // Match SimulationVisualBot3D exactly: BoxVisual3D with Length=Radius*2, Width=Radius*2, Height=Radius*0.9
            // Bot center Z = tierZ + _height/2 where _height = Radius*0.9
            double height  = bot.Radius * 0.9;
            double centerZ = z + height / 2.0;
            var mb = new MeshBuilder(true, false);
            mb.AddBox(new Point3D(bot.X, bot.Y, centerZ), bot.Radius * 2, bot.Radius * 2, height);

            var specCol = Color.FromRgb(
                (byte)Math.Min(255, col.R + 80),
                (byte)Math.Min(255, col.G + 80),
                (byte)Math.Min(255, col.B + 80));
            var mat = new MaterialGroup();
            mat.Children.Add(new DiffuseMaterial(new SolidColorBrush(col)));
            mat.Children.Add(new SpecularMaterial(new SolidColorBrush(specCol), 40));
            return new GeometryModel3D(mb.ToMesh(), mat) { BackMaterial = mat };
        }

        private Model3DGroup _emptyGroup = null!;

        public byte[] RenderBot(BotNormal bot)
        {
            byte[] result = Array.Empty<byte>();

            _dispatcher!.Invoke(() =>
            {
                // Rebuild pod scene every frame — pods move with bots when carried
                _podDynamicVisual.Content = BuildCurrentPodScene();

                // Update all bot positions; hide the rendering bot so camera isn't inside its box
                ModelVisual3D? ownVisual = null;
                foreach (var (b, visual) in _botVisuals)
                {
                    if (b == bot)
                    {
                        ownVisual = visual;
                        visual.Content = _emptyGroup; // hide self
                    }
                    else
                    {
                        visual.Content = MakeBotModel(b);
                    }
                }

                // Position camera matching Visualization UI formula: radius*0.9 + 0.05
                double eyeHeight = bot.Radius * 0.9 + 0.05;
                double orient    = bot.GetInfoOrientation();
                double tierZ     = bot.Tier.RelativePositionZ;
                var cam = (PerspectiveCamera)_viewport!.Camera;
                cam.Position      = new Point3D(bot.X, bot.Y, tierZ + eyeHeight);
                cam.LookDirection = new Vector3D(Math.Cos(orient), Math.Sin(orient), 0);
                cam.UpDirection   = new Vector3D(0, 0, 1);
                cam.FieldOfView   = _config.FieldOfViewDeg;

                _viewport.InvalidateVisual();
                _viewport.UpdateLayout();

                var rtb = new RenderTargetBitmap(
                    _config.ImageWidth, _config.ImageHeight,
                    96, 96, PixelFormats.Pbgra32);
                rtb.Render(_viewport);

                // BGRA → RGB
                int pixelCount   = _config.ImageWidth * _config.ImageHeight;
                byte[] bgraBytes = new byte[pixelCount * 4];
                rtb.CopyPixels(bgraBytes, _config.ImageWidth * 4, 0);

                result = new byte[pixelCount * 3];
                for (int i = 0; i < pixelCount; i++)
                {
                    result[i * 3 + 0] = bgraBytes[i * 4 + 2]; // R
                    result[i * 3 + 1] = bgraBytes[i * 4 + 1]; // G
                    result[i * 3 + 2] = bgraBytes[i * 4 + 0]; // B
                }

                // Restore own cylinder
                if (ownVisual != null)
                    ownVisual.Content = MakeBotModel(bot);
            });

            return result;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _dispatcher?.Invoke(() => _hiddenWindow?.Close());
                _dispatcher?.InvokeShutdown();
            }
        }
    }
}
