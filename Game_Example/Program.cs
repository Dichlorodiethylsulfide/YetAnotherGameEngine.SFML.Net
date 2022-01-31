using ECS;
using ECS.Window;
using ECS.Graphics;
using ECS.Library;
using ECS.Maths;
using ECS.Physics;

using System;

namespace Game_Example
{
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
        private const int Size = 32;
        private const string Placeholder = @"C:\Users\rikil\Desktop\Backupable\Coding\C#\YetAnotherGameEngine.SFML.Net\Resources\Placeholder\Placeholder Block 2.png";
        public override EngineSettings Settings => new EngineSettings(64, 1024000, 1024000, 64, new SFML.Window.VideoMode(1280, 720), "Engine Window");
        public override void Initialise()
        {
            Texture texture = new Texture(Placeholder);


            var counter = 10;

            for (int x = 0; x < counter; x++)
            {
                var cObject = CObject.New();
                cObject.AddData(texture);
                cObject.AddData(new Transform(x * Size * 2, 0));
                cObject.AddData(new PhysicsBody());
            }
        }
    }

}
