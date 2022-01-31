using ECS;
using ECS.Window;
using ECS.Graphics;
using ECS.Library;

namespace Game_Example
{
    internal class Program
    {
        static void Main(string[] args)
        {
            System.Console.WriteLine("Hello World!");
            Engine.Start(typeof(GameEngine));
            System.Console.WriteLine("Goodbye World!");
        }
    }
    internal class GameEngine : Engine
    {
        private const string Placeholder = @"C:\Users\rikil\Desktop\Backupable\Coding\C#\YetAnotherGameEngine.SFML.Net\Resources\Placeholder\Placeholder Block 2.png";
        public override EngineSettings Settings => new EngineSettings(4, 1024, 1024, 10, new SFML.Window.VideoMode(800, 600), "Engine Window");
        public override void Initialise()
        {
            Texture texture = new Texture(Placeholder);

            for (int y = 0; y < 10; y++)
            {
                for (int x = 0; x < 10; x++)
                {
                    var cObject = CObject.New();
                    cObject.AddData(texture);
                    cObject.AddData(new Transform(x * 32, y * 32));
                }
            }

            var player2 = CObject.New();
            player2.AddData(texture);
            player2.AddData(new Transform(500, 500));
            player2.AddData(new PlayerData());

            
        }
    }

}
