#pragma warning disable IDE0022, IDE0009

using ECS; // Main namespace
using ECS.Graphics; // Includes textures and transforms
using ECS.Window; // Includes engine window / input events
using ECS.Maths; // Includes maths equations and constants
using ECS.Physics; // Includes physics bodies

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
    public static class Time
    {
        public static float DeltaTime { get; internal set; } = 0f;
    }
    public abstract class Engine
    {

        internal static Engine MainEngine = null;
        internal static EngineWindow MainWindow => MainEngine?.ThisWindow;


        internal EngineWindow ThisWindow = null;
        public abstract void Initialise();
        public virtual EngineSettings Settings => new EngineSettings(4, 1024, 1024, 10, new Vector2u(800, 600), "Window", false, false);
        private void GameLoop() // might make "public virtual"
        {
            for (int i = 0; i < Collection.Subsystems.Count; i++)
            {
                var subsystem = Collection.Subsystems[i];
                if (subsystem.IsEnabled)
                {
                    var time = DateTime.Now;
                    subsystem.Update(Time.DeltaTime);
                    var now = DateTime.Now;
                    Console.WriteLine(subsystem.Name + ": " + ( now - time ).TotalMilliseconds);
                }
            }
        }
        public static void Start(Type engineType)
        {
            if (engineType.IsSubclassOf(typeof(Engine)))
            {
                MainEngine = (Engine)Activator.CreateInstance(engineType);
                MainEngine.ThisWindow = new EngineWindow(new VideoMode(MainEngine.Settings.WindowDimensions), MainEngine.Settings.WindowName);
                Entry(MainEngine.Settings.MainTableSize, MainEngine.Settings.ObjectTableSize, MainEngine.Settings.DataTableSize, MainEngine.Settings.TextureTableSize);
                Collection.AddNewSubsystem(typeof(RenderSubsystem));
                AddNewDataType<Texture>();
                AddNewDataType<Transform>();
                if (MainEngine.Settings.EnablePhysics)
                {
                    AddNewDataType<PhysicsBody>();
                    Collection.AddNewSubsystem(typeof(PhysicsSubsystem));
                }
                if(MainEngine.Settings.EnableCollisions)
                {
                    AddNewDataType<Collider>();
                    Collection.AddNewSubsystem(typeof(CollisionSubsystem));
                }
                // Other
                Collection.AddNewSubsystem(typeof(ColourGradientSubsystem));
                MainEngine.Initialise();
                while (MainWindow.IsOpen)
                {
                    MainWindow.DispatchEvents();
                    var time = DateTime.Now;
                    MainEngine.GameLoop();
                    MainWindow.Display();
                    MainWindow.Clear();
                    Time.DeltaTime = (float)( DateTime.Now - time ).TotalSeconds;
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
        public EngineSettings(int mSize, int otSize, int dtSize, int texSize, Vector2u window_dimensions, string name, bool enable_physics, bool enable_collisions)
        {
            MainTableSize = mSize;
            ObjectTableSize = otSize;
            DataTableSize = dtSize;
            TextureTableSize = texSize;
            WindowDimensions = window_dimensions;
            WindowName = name;
            EnablePhysics = enable_physics;
            EnableCollisions = enable_collisions;
        }
    }
    public static class Collection
    {
        internal static readonly List<Subsystem> Subsystems = new List<Subsystem>();
        internal static void AddNewSubsystem(Type type)
        {
            if (type.IsSubclassOf(typeof(Subsystem)) && !Subsystems.Any(x => x.GetType() == type))
            {
                Subsystems.Add((Subsystem)Activator.CreateInstance(type));
            }
        }
    }
    public abstract class Subsystem
    {
        public static RefObjectTable Entities => UnmanagedCSharp.Entities;
        public static void AddNewDataType<T>() where T : unmanaged => UnmanagedCSharp.AddNewDataType<T>();
        public static void AddNewSubsystem(Type type) => Collection.AddNewSubsystem(type);

        public Subsystem()
        {
            Name = GetType().Name;
        }
        public readonly string Name;
        public bool IsEnabled { get; set; } = true;
        public abstract void Update(float deltaSeconds);
    }
    public sealed class RenderSubsystem : Subsystem
    {
        public override void Update(float deltaSeconds)
        {
            Entities.Iterate((ref Texture texture, ref Transform transform) =>
            {
                var states = new RenderStates(transform.SFMLTransform);
                texture.Draw(Engine.MainWindow, states);
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

    public class CollisionSubsystem : Subsystem
    {
        public override void Update(float deltaSeconds)
        {
            List<Collision> collisions = Entities.CollisionQuery();
            Console.WriteLine(collisions.Count);
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
