using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WebCrawler.WebCrawler
{
    public class WebCrawlResult
    {
        public long TotalSites { get; set; }
        public long TotalUniqueSites { get; set; }
        public long TotalUnfound { get; set; }
        public long TotalResponsiveSites { get; set; }
        public TimeSpan TotalTime { get; set; }
        public long TotalBytesDownloaded { get; set; }

        public void PrintResult()
        {
            Console.WriteLine($"Total Sites in file: {TotalSites}");
            Console.WriteLine($"Total unique sites: {TotalUniqueSites}");
            Console.WriteLine($"Replies from: {TotalResponsiveSites}");
            Console.WriteLine($"Hosts not found: {TotalUnfound}");
            Console.WriteLine();
            Console.WriteLine($"Bytes downloaded: {TotalBytesDownloaded}");
            Console.WriteLine($"Total time: {TotalTime.ToString()}");
        }
    }
}
