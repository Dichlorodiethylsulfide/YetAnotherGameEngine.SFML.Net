using ECSLibrary;

using SFML.Graphics;

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
        public override void Initialise()
        {
            /*
            for(int y = 0; y < 10; y++)
            {
                for(int x = 0; x < 10; x++)
                {
                    var sprite = new Sprite(new Texture(@"C:\Users\rikil\Desktop\Backupable\Coding\C#\YetAnotherGameEngine.SFML.Net\Resources\Placeholder\Placeholder Block.png"))
                    {
                        Position = new SFML.System.Vector2f(x * 32, y * 32)
                    };
                    Object.New(new RenderData(sprite));
                }
            }
            */
        }
    }

}
