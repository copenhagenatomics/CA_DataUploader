using CA_DataUploaderLib;
using System;

namespace ShowAvailableMCUboards
{
    class Program
    {
        static void Main(string[] args)
        {
            var ports = new SerialNumberMapper(true);

            foreach(var board in ports.McuBoards)
            {
                Console.WriteLine("--------------------------------------------");
                Console.WriteLine(board.ToString(Environment.NewLine));

                if(board.IsOpen)
                {
                    for(int i=0; i<4; i++)
                        Console.WriteLine(board.ReadLine());
                }
            }
        }
    }
}
