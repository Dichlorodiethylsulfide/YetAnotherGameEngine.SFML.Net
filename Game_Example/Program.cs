using ECS;
using ECS.Window;
using ECS.Graphics;
using ECS.Library;
using ECS.Maths;
using ECS.Physics;

using System;

namespace Game_Example
{
    using static Subsystem;
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
            Engine.Start(typeof(GameEngine));
            Console.WriteLine("Goodbye World!");
        }
    }
    internal class GameEngine : Engine
    {
        private const string Placeholder = @"C:\Users\rikil\Desktop\Backupable\Coding\C#\YetAnotherGameEngine.SFML.Net\Resources\Placeholder\Placeholder Block 2.png";
        public override EngineSettings Settings => new EngineSettings(64, 1024000, 1024000, 64, new SFML.System.Vector2u(1280, 720), "Engine Window", false, false);
        public override void Initialise()
        {
            AddNewSubsystem(typeof(TestSubsystem));
            AddNewDataType<TestData>();

            Texture texture = new Texture(Placeholder);

            var counter = 100;

            for (int y = 0; y < counter; y++)
            {
                for(int x = 1; x < counter + 1; x++)
                {
                    var cObject = CObject.New();
                    cObject.AddData(texture);
                    cObject.AddData(new Transform(x * 33, y * 33, 32, 32));
                    cObject.AddData(new TestData(100));
                    //cObject.AddData(new PhysicsBody());
                    //cObject.AddData(new Collider());
                }
            }
        }
    }
    internal class TestSubsystem : Subsystem
    {
        public override void Update(float deltaSeconds)
        {
            Entities.Iterate((ref TestData data) =>
            {
                data.Health++;
            }); 
        }
    }
    internal struct TestData : IComponentData
    {
        public int Health;
        public TestData(int health) => Health = health;
    }
}
