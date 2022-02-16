/*#pragma warning disable IDE0022, IDE0009

using ECS; // Main namespace
using ECS.Graphics; // Includes textures and transforms
using ECS.Maths; // Includes maths equations and constants
using ECS.Physics; // Includes physics bodies
using ECS.UI;

using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using SFML.System;
using SFML.Graphics;
using SFML.Window;

using System.Diagnostics.CodeAnalysis;


namespace ECS.Library
{
    using static UnmanagedCSharp;
    using static Keyboard;
    public static class Input
    {
        private static readonly Current CurrentInput = new Current();
        internal static bool GetInputBool<Args>(this Args args, Func<Args, bool> predicate) where Args : EventArgs => args != null && predicate(args);
        internal static void SetMouseButtonPressedArgs(MouseButtonEventArgs e) => CurrentInput.MouseButtonPressed = e;
        internal static void SetMouseButtonReleasedArgs(MouseButtonEventArgs e) => CurrentInput.MouseButtonPressed = null;
        internal static void SetKeyPressedArgs(KeyEventArgs e) => CurrentInput.KeyPressed.Add(e);
        internal static void SetKeyReleasedArgs(KeyEventArgs e) => CurrentInput.KeyPressed.RemoveWhere(x => x.Code == e.Code);
        public static bool GetMouseButtonPressed(Mouse.Button mouseButton) => CurrentInput.MouseButtonPressed.GetInputBool((x) => x.Button == mouseButton);
        public static bool GetKeyPressed(Key key) => CurrentInput.KeyPressed.Any(x => x.Code == key);
        public static Vector2f GetMousePosition() => (Vector2f)Mouse.GetPosition(Engine.MainWindow);
        private class Current
        {
            public JoystickButtonEventArgs JoystickButtonPressed = null;
            public TouchEventArgs TouchMoved = null;
            public TouchEventArgs TouchBegan = null;
            public JoystickConnectEventArgs JoystickDisconnected = null;
            public SizeEventArgs Resized = null;
            public JoystickConnectEventArgs JoystickConnected = null;
            public JoystickButtonEventArgs JoystickButtonReleased = null;
            public TextEventArgs TextEntered = null;
            public KeyEventArgs KeyReleased = null;
            public MouseWheelScrollEventArgs MouseWheelScrolled = null;
            public MouseButtonEventArgs MouseButtonPressed = null;
            public JoystickMoveEventArgs JoystickMoved = null;
            public MouseMoveEventArgs MouseMoved = null;
            public Multi<KeyEventArgs> KeyPressed = new Multi<KeyEventArgs>();
            public MouseButtonEventArgs MouseButtonReleased = null;
            public SensorEventArgs SensorChanged = null;
            public TouchEventArgs TouchEnded = null;
        }
        private class Multi<Args> where Args : EventArgs
        {
            private readonly List<Args> argsList = new List<Args>();

            public void Add(Args args) => argsList.Add(args);
            public bool Remove(Args args) => argsList.Remove(args);
            public int RemoveWhere(Predicate<Args> predicate) => argsList.RemoveAll(predicate);
            public void Clear() => argsList.Clear();
            public bool Any(Func<Args, bool> predicate) => argsList.Any(predicate);
        }
    }
    public abstract class Engine
    {
        public class EngineWindow : RenderWindow
        {
            public static Collision.AABB GetWindowBoundingBox => new Collision.AABB(0, 0, MainEngine.Settings.WindowDimensions.X, MainEngine.Settings.WindowDimensions.Y);
            public EngineWindow(VideoMode mode, string name, uint frame_rate) : base(mode, name)
            {
                SetFramerateLimit(frame_rate);
                this.Closed += OnQuit;
                this.KeyPressed += OnKeyPress;
                this.KeyReleased += OnKeyRelease;
                this.MouseButtonPressed += OnMousePress;
                this.MouseButtonReleased += OnMouseRelease;
            }

            private void OnQuit(object sender, EventArgs args)
            {
                this.Close();
            }

            private void OnKeyPress(object sender, KeyEventArgs args)
            {
                Input.SetKeyPressedArgs(args);
            }

            private void OnKeyRelease(object sender, KeyEventArgs args)
            {
                Input.SetKeyReleasedArgs(args);
            }

            private void OnMousePress(object sender, MouseButtonEventArgs args)
            {
                Input.SetMouseButtonPressedArgs(args);
            }
            private void OnMouseRelease(object sender, MouseButtonEventArgs args)
            {
                Input.SetMouseButtonReleasedArgs(args);
            }
        }

        internal static Engine MainEngine = null;
        internal static EngineWindow MainWindow => MainEngine?.ThisWindow;


        internal EngineWindow ThisWindow = null;

        public abstract void Initialise();
        public virtual EngineSettings Settings => new EngineSettings(4, 1024, 1024, 10, new Vector2u(800, 600), "Window", false, false, false);
        private void GameLoop() // might make "public virtual"
        {
            var delta = CTime.DeltaTime;
            for (int i = 0; i < Collection.Subsystems.Count; i++)
            {
                var subsystem = Collection.Subsystems[i];
                if (subsystem.IsEnabled)
                {
                    var time = DateTime.Now;
                    subsystem.Update(delta);
                    var now = DateTime.Now;
                    Console.WriteLine(subsystem.Name + ": " + ( now - time ).TotalMilliseconds);
                }
            }
            Thread.Sleep((int)(CTime.DeltaTime * 1000));
        }
        public static void Stop()
        {
            if(MainEngine != null)
            {
                MainWindow.Close();
            }
        }
        public static void Start(Type engineType)
        {
            if (engineType.IsSubclassOf(typeof(Engine)))
            {
                MainEngine = (Engine)Activator.CreateInstance(engineType);
                
                Entry(MainEngine.Settings.MainTableSize, MainEngine.Settings.ObjectTableSize, MainEngine.Settings.DataTableSize, MainEngine.Settings.TextureTableSize);
                Collection.AddNewSubsystem<RenderSubsystem>();
                Collection.AddNewSubsystem<UISubsystem>();

                if (MainEngine.Settings.EnablePhysics)
                {
                    AddNewDataType<PhysicsBody>();
                    Collection.AddNewSubsystem<PhysicsSubsystem>();
                }
                if (MainEngine.Settings.EnableCollisions)
                    AddNewDataType<Collider>();
                if (MainEngine.Settings.EnableBoundary)
                    Collection.AddNewSubsystem<BoundarySubsystem>();

                MainEngine.Initialise();

                bool foundCollisionSubsystem = Collection.Subsystems.Any(x => x.GetType().IsSubclassOf(typeof(CollisionSubsystem)));

                if(MainEngine.Settings.EnableCollisions && !foundCollisionSubsystem)
                    throw new Exception("Please provide implementation of collision subsystem.");
                if(!MainEngine.Settings.EnableCollisions && foundCollisionSubsystem)
                    throw new Exception("Cannot initialise collision subsystem if collision is disabled.");

                Subsystem.SystemFlags.SetFlags();

                foreach (var item in Collection.Subsystems)
                    if (item.IsEnabled)
                        item.Startup();

                while (MainEngine.ThisWindow is null && CTime.DeltaTime == 0f)
                    ;
                while (MainWindow.IsOpen)
                {
                    MainEngine.GameLoop();
                }
            }
        }
    }
    public struct EngineSettings
    {
        public int MainTableSize;
        public int ObjectTableSize;
        public int DataTableSize;
        public int TextureTableSize;
        public Vector2u WindowDimensions;
        public string WindowName;
        public bool EnablePhysics;
        public bool EnableCollisions;
        public bool EnableBoundary;
        public uint DesiredFrameRate;
        public EngineSettings(int mSize, int otSize, int dtSize, int texSize, Vector2u window_dimensions, string name, bool enable_physics, bool enable_collisions, bool enable_boundary, uint desiredFrameRate = 60)
        {
            MainTableSize = mSize;
            ObjectTableSize = otSize;
            DataTableSize = dtSize;
            TextureTableSize = texSize;
            WindowDimensions = window_dimensions;
            WindowName = name;
            EnablePhysics = enable_physics;
            EnableCollisions = enable_collisions;
            EnableBoundary = enable_boundary;
            DesiredFrameRate = desiredFrameRate;
        }
    }
    public static class Collection
    {
        internal static readonly List<Subsystem> Subsystems = new List<Subsystem>();
        internal static void AddNewSubsystem<System>() where System : Subsystem
        {
            var type = typeof(System);
            if (!type.IsAbstract && !Subsystems.Any(x => x.GetType() == type))
            {
                Subsystems.Add((Subsystem)Activator.CreateInstance(type));
            }
        }
    }
    public abstract class Subsystem
    {
        internal struct SubsystemFlags
        {
            public bool AddNewDataTypes;
            public bool AddNewSubsystems;
            public SubsystemFlags(bool add_data, bool add_systems)
            {
                AddNewDataTypes = add_data;
                AddNewSubsystems = add_systems;
            }
            public void SetFlags()
            {
                AddNewDataTypes = false;
                AddNewSubsystems = false;
            }
        }
        internal static SubsystemFlags SystemFlags = new SubsystemFlags(true, true);
        public static RefObjectTable Entities => UnmanagedCSharp.Entities;
        public static void AddNewDataType<T>() where T : unmanaged
        {
            if (SystemFlags.AddNewDataTypes) // throw on fail
                UnmanagedCSharp.AddNewDataType<T>();
        }
        public static void AddNewSubsystem<System>() where System : Subsystem
        {
            if(SystemFlags.AddNewSubsystems) // throw on fail
                Collection.AddNewSubsystem<System>();
        }

        public Subsystem()
        {
            Name = GetType().Name;
        }
        public readonly string Name;
        public bool IsEnabled { get; set; } = true;
        public abstract void Update(float deltaSeconds);
        public virtual void Startup() { }
    }
    public struct BulletData : IComponentData
    {
        public int Damage;
        public Vector2f Direction;
        public float Speed;
        public float LifeTime;
        public BulletData(Vector2f dir, float speed, int dmg = 10)
        {
            Damage = dmg;
            Direction = dir;
            Speed = speed;
            LifeTime = 2f;
        }
    }
    
    public sealed class RenderSubsystem : Subsystem
    {
        private Thread RenderThread = null;
        public override void Startup()
        {
            IsEnabled = false;
            RenderThread = new Thread(() => specialCaseUpdate());
            RenderThread.Start();
        }
        public override void Update(float deltaSeconds) { }
        internal void specialCaseUpdate()
        {

            Engine.MainEngine.ThisWindow = new Engine.EngineWindow(new VideoMode(Engine.MainEngine.Settings.WindowDimensions), Engine.MainEngine.Settings.WindowName, Engine.MainEngine.Settings.DesiredFrameRate);

            while (Engine.MainWindow.IsOpen)
            {
                Engine.MainWindow.DispatchEvents();

                var time = DateTime.Now;

                Entities.Iterate((ref Texture texture, ref Transform transform) =>
                {
                    var states = new RenderStates(transform.SFMLTransform);
                    texture.Draw(Engine.MainWindow, states);
                });

                Entities.Iterate((ref UI.Text text, ref Transform transform) =>
                {
                    var states = new RenderStates(transform.SFMLTransform);
                    text.Draw(Engine.MainWindow, states);
                });

                Engine.MainWindow.Display();
                Engine.MainWindow.Clear();

                CTime.DeltaTime = (float)((DateTime.Now - time).TotalSeconds);

                Console.WriteLine(Name + ": " + (CTime.DeltaTime * 1000).ToString());
            }
        }
    }
    public sealed class BoundarySubsystem : Subsystem
    {
        public override void Update(float deltaSeconds)
        {
            Entities.Iterate((ref Transform transform) =>
            {
                var window = Engine.MainEngine.Settings.WindowDimensions;
                var current_x = transform.Position.X;
                var current_y = transform.Position.Y;
                if (current_x < 0)
                    current_x = 0;
                else if (current_x > window.X)
                    current_x = window.X;

                if (current_y < 0)
                    current_y = 0;
                else if (current_y > window.Y)
                    current_y = window.Y;

                transform.Position = new Vector2f(current_x, current_y);
            });
        }
    }

    public class PhysicsSubsystem : Subsystem
    {
        private Vector2u Window = Engine.MainEngine.Settings.WindowDimensions;
        public override void Update(float deltaSeconds)
        {
            Entities.Iterate((ref PhysicsBody body, ref Transform transform) =>
            {
                var size = transform.Size / 2;
                
                body.Velocity += Constants.Gravity * deltaSeconds;

                if(transform.Position.Y + size.Y > Window.Y || transform.Position.Y < 0)
                {
                    body.Velocity = new Vector2f(body.Velocity.X, -1 * body.Velocity.Y);
                }

                if (transform.Position.X + size.X > Window.X || transform.Position.X < 0)
                {
                    body.Velocity = new Vector2f(-1 * body.Velocity.X, body.Velocity.Y);
                }

                transform.Position += body.Velocity * deltaSeconds;
            });
        }
    }

    public abstract class CollisionSubsystem : Subsystem
    {
        public virtual bool VerifyCollisions { get; protected set; } = false;
        public static Dictionary<CObject, List<CObject>> GetPossibleCollisions => UnmanagedCSharp.PossibleCollisionsThisFrame;
        public static Dictionary<CObject, List<CObject>> GetVerifiedCollisions => UnmanagedCSharp.VerifiedCollisionsThisFrame;
        public override void Update(float deltaSeconds)
        {
            Entities.PossibleCollisionQuery();
            if(VerifyCollisions)
            {
                Entities.VerifyCollisions(Engine.MainEngine.Settings.EnablePhysics);
            }
        }
    }

    public class UISubsystem : Subsystem
    {
        public override void Update(float deltaSeconds)
        {
            Entities.Iterate((ref Button button, ref Transform transform) =>
            {
                if (Input.GetMouseButtonPressed(0) && transform.BoundingBox.Contains(Input.GetMousePosition()))
                    button.Click();
            });
        }
    }

    #region Other Subsystems
    public class MovementSubsystem : Subsystem
    {
        public override void Update(float deltaSeconds)
        {
            Entities.Iterate((ref Transform transform) =>
            {
                transform.Position += new Vector2f(0.1f, 0.1f);
            });
        }
    }
    public class ColorRandomiserSubsystem : Subsystem
    {
        private float Timer = 0f;
        public override void Update(float deltaSeconds)
        {
            Timer += deltaSeconds;
            if (Timer > 1f)
            {
                Entities.Iterate((ref Texture texture) =>
                {
                    texture.RandomiseColor();
                });
                Timer = 0f;
            }
        }
    }
    public class ColourGradientSubsystem : Subsystem
    {
        public override void Update(float deltaSeconds)
        {
            Entities.Iterate((ref Texture texture, ref Transform transform) =>
            {
                var height = (byte)Math.Clamp(transform.Position.Y, 0, 255);

                texture.SetColor(new Color(height, height, height));
            });
        }
    }
    #endregion Other Subsystems

}
*/