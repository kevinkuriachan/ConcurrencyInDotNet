using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using WebCrawler.WebCrawler;

namespace WebCrawler
{
    public enum RunType
    {
        SingleThreaded,
        AwaitAll,
        InterlockedAwaitAll,
        PC
    }
    internal class Program
    {
        public static async Task Main(string[] args)
        {
            if (args.Length != 3 && args.Length != 4)
            {
                Console.WriteLine("Usage: WebCrawler.exe filename maxLines concurrencyMode [numThreads]");
                return;
            }

            string filename = args[0];

            if (!int.TryParse(args[1], out int maxLines))
            {
                Console.WriteLine("Max lines is not a valid integer");
                return;
            }

            Console.WriteLine($"Input filename: {filename}");

            if (!File.Exists(filename))
            {
                Console.WriteLine($"File does not exist: {filename}");
                return;
            }

            Console.WriteLine("Reading file and crawling urls");

            IWebCrawler webCrawler;
            if (!Enum.TryParse<RunType>(args[2], true, out RunType runType))
            {
                Console.WriteLine("Incorrect concurrency mode");
                return;
            }

            Console.WriteLine($"Run type: {runType.ToString()}");

            switch (runType)
            {
                case RunType.SingleThreaded:
                    webCrawler = new WebCrawlerSingleThreaded(filename);
                    break;
                case RunType.AwaitAll:
                    webCrawler = new WebCrawlerAwaitAll(filename);
                    break;
                case RunType.InterlockedAwaitAll:
                    webCrawler = new WebCrawlerInterlockedAwaitAll(filename);
                    break;
                case RunType.PC:
                    if (args.Length != 4)
                    {
                        Console.WriteLine($"numThreads required when using mode {runType}");
                    }
                    if (!int.TryParse(args[3], out int numThreads))
                    {
                        Console.WriteLine("numThreads is not a valid integer");
                        return;
                    }
                    webCrawler = new WebCrawlerPC(filename, numThreads);
                    break;
                default:
                    webCrawler = new WebCrawlerSingleThreaded(filename);
                    break;
            }

            //IWebCrawler webCrawler = new WebCrawlerSingleThreaded(filename);

            //IWebCrawler webCrawler = new WebCrawlerAwaitAll(filename);

            //IWebCrawler webCrawler = new WebCrawlerInterlockedAwaitAll(filename);

            WebCrawlResult result = await webCrawler.CrawlAsync(maxLines);

            result.PrintResult();

            Console.ReadKey();
        }
    }
}
