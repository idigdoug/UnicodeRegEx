namespace msandbox
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using RepStrRegEx;

    internal class Program
    {
        static void Main(string[] args)
        {
            try
            {
                var regex = RegEx.Create("pat*ern");

                foreach (var match in regex.MatchEnumerator(
                    "Input patterns and paerns",
                    formatTemplate: "<$0-$0>"))
                {
                    Console.WriteLine(match.Format());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }
    }
}
