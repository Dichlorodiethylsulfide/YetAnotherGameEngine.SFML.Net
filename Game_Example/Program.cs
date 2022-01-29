using ECS;
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
        private const string Placeholder = @"C:\Users\rikil\Desktop\Backupable\Coding\C#\YetAnotherGameEngine.SFML.Net\Resources\Placeholder\Placeholder Block.png";
        public override void Initialise()
        {

            /*Texture texture = new Texture(Placeholder);
            for (int y = 0; y < 10; y++)
            {
                for(int x = 0; x < 10; x++)
                {
                    //CObject.New(new Texture(Placeholder), new Transform(x * 32, y * 32));
                    var cObject = CObject.New();
                    Collection.AddNewObject(cObject);
                    Collection.AddObjectComponentData(cObject, texture);
                    Collection.AddObjectComponentData(cObject, new Transform(x * 32, y * 32));
                }
                System.Console.WriteLine(y);
            }*/

            //Collection.AddNewBatch(1000, new Texture(Placeholder), new Transform());
            //var player = Object.New(new Texture(Placeholder), new Transform(0, 0), new PlayerData(100));
            //var player1 = Object.New(new Texture(Placeholder), new Transform(1, 1), new PlayerData(100));


            Texture texture = new Texture(Placeholder);
            for (int y = 0; y < 10; y++)
            {
                for (int x = 0; x < 10; x++)
                {
                    //CObject.New(new Texture(Placeholder), new Transform(x * 32, y * 32));
                    var cObject = CObject.New();
                    cObject.AddData(texture);
                    cObject.AddData(new Transform(x * 32, y * 32));

                }
                //System.Console.WriteLine(y);
            }
        }
    }

}
