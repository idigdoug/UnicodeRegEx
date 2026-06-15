namespace msandbox
{
    using System;
    using UnicodeRegEx;

    internal class Program
    {
        static void Main(string[] args)
        {
            try
            {
                var regex = RegEx.Create("pat*ern");
                regex.EnumerateMatches(
                    "Input patterns and paerns",
                    new RegExEnumerateOptions { FormatTemplate = "<$0-$0>" },
                    matches =>
                {
                    int count = 0;
                    foreach (var match in matches)
                    {
                        Console.WriteLine($"Match: {match.Format()}");
                        count++;
                    }

                    return count;
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }
    }
}
