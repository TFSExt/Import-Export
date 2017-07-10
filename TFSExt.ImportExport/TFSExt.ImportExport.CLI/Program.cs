using System;
using TFSExt.ImportExport.Core;

namespace TFSExt.ImportExport.CLI
{
	class Program
	{
		static void Main(string[] args)
		{
			Console.Write("Enter source account url: ");
			var sourceContextUrl = Console.ReadLine();
			Console.WriteLine("Connecting to {0}", sourceContextUrl);
			var sourceContext = new TfsContext(sourceContextUrl);

			Console.Write("Enter source project name: ");
			var sourceProjectName = Console.ReadLine();

			Console.WriteLine("Querying work items from source project");
			var query = $"SELECT {TfsContext.ID} FROM WorkItems WHERE [System.TeamProject] = '{sourceProjectName}'";
			var items = sourceContext.GetWorkItemsAsync(query).Result;
			Console.WriteLine("Found {0} work items to copy", items.Count);

			Console.Write("Enter destination account url: ");
			var destinationContextUrl = Console.ReadLine();
			Console.WriteLine("Connecting to {0}", destinationContextUrl);
			var destinationContext = new TfsContext(destinationContextUrl);

			Console.Write("Enter destination project name: ");
			var destinationProjectName = Console.ReadLine();

			Console.WriteLine("Copying started");
			destinationContext.CreateWorkItemsAsync(items, destinationProjectName).Wait();
			Console.WriteLine("Copying finished");

			Console.WriteLine("Live long and prosper!");
			Console.WriteLine("Press 'enter' to close");
			Console.ReadLine();
		}
	}
}
