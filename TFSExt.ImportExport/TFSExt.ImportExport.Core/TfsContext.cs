using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Client;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.WebApi.Patch;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TFSExt.ImportExport.Core
{
	public sealed class TfsContext
	{
		public const string FORWARD = "System.LinkTypes.Hierarchy-Forward";
		public const string ID = "System.Id";
		public const string TITLE = "System.Title";
		public const string TYPE = "System.WorkItemType";

		private readonly string _url;
		private readonly VssConnection _connection;

		public TfsContext(string url)
		{
			_url = url;
			_connection = new VssConnection(new Uri(_url), new VssClientCredentials());
		}

		#region public

		public async Task<List<WorkItem>> GetWorkItemsAsync(string query)
		{
			using (var client = _connection.GetClient<WorkItemTrackingHttpClient>())
			{
				return await QueryWorkItemsAsync(client, query);
			}
		}

		public async Task CreateWorkItemsAsync(List<WorkItem> workItems, string project)
		{
			using (var client = _connection.GetClient<WorkItemTrackingHttpClient>())
			{
				var tasks = new List<Task>();

				foreach (var workItem in workItems)
				{
					var task = GetCreateWorkItemTask(client, workItem, project);

					tasks.Add(task);
				}

				await Task.WhenAll(tasks);

				await LinkWorkItemsAsync(client, workItems);
			}
		}

		public async Task LinkWorkItemsAsync(WorkItemTrackingHttpClient client, List<WorkItem> workItems)
		{
			var workItemsWithRelations = workItems
				.Where(x => x.Relations != null)
				.ToList();

			if (!workItemsWithRelations.Any())
			{
				return;
			}

			var tasks = new List<Task>();

			foreach (var workItemWithRelation in workItemsWithRelations)
			{
				var forwardRelations = workItemWithRelation.Relations
					.Where(x => x.Rel == FORWARD);

				foreach (var forwardRelation in forwardRelations)
				{
					var targetWorkItemId = forwardRelation.Url.Split('/').Last();
					var targetWorkItem = workItems.Single(x => x.Fields[ID].ToString() == targetWorkItemId);

					var newWorkItemWithRelation = await FindWorkItemByTitleAsync(
						client, (string)workItemWithRelation.Fields[TITLE], (string)workItemWithRelation.Fields[TYPE]);
					var newTargetWorkItem = await FindWorkItemByTitleAsync(
						client, (string)targetWorkItem.Fields[TITLE], (string)targetWorkItem.Fields[TYPE]);

					var task = GetLinkWorkItemsTask(client, newWorkItemWithRelation.Id.Value, newTargetWorkItem);
					tasks.Add(task);
				}
			}

			await Task.WhenAll(tasks);
		}

		#endregion

		#region private

		private Task GetCreateWorkItemTask(WorkItemTrackingHttpClient client, WorkItem workItem, string project)
		{
			var newWorkItem = new JsonPatchDocument();

			var fieldsToCopy = new string[]
			{
				TITLE,
				"System.Description",
				"Microsoft.VSTS.Scheduling.RemainingWork"
			};
			foreach (var fieldName in fieldsToCopy)
			{
				var field = CreateWorkItemField(workItem, fieldName);
				if (field != null)
				{
					newWorkItem.Add(field);
				}
			}

			var tags = CreateTags(workItem);
			if (tags != null)
			{
				newWorkItem.Add(tags);
			}

			return client.CreateWorkItemAsync(newWorkItem, project, (string)workItem.Fields[TYPE]);
		}

		private Task GetLinkWorkItemsTask(WorkItemTrackingHttpClient client, int workItemId, WorkItem targetWorkItem)
		{
			JsonPatchDocument patchDocument = new JsonPatchDocument();

			patchDocument.Add
			(
				new JsonPatchOperation()
				{
					Operation = Operation.Add,
					Path = "/relations/-",
					Value = new
					{
						rel = FORWARD,
						url = targetWorkItem.Url
					}
				}
			);

			return client.UpdateWorkItemAsync(patchDocument, workItemId);
		}

		private async Task<List<WorkItem>> QueryWorkItemsAsync(WorkItemTrackingHttpClient client, string query)
		{
			var _query = new Wiql();
			_query.Query = query;

			var ids = (await client
				.QueryByWiqlAsync(_query))
				.WorkItems
				.Select(x => x.Id);

			return await client.GetWorkItemsAsync(ids, expand: WorkItemExpand.All);
			
		}

		private JsonPatchOperation CreateWorkItemField(WorkItem workItem, string fieldName)
		{
			if (!workItem.Fields.ContainsKey(fieldName))
			{
				return null;
			}

			return new JsonPatchOperation
			{
				Operation = Operation.Add,
				Path = "/fields/" + fieldName,
				Value = workItem.Fields[fieldName]
			};
		}

		private JsonPatchOperation CreateTags(WorkItem workItem)
		{
			var tags = new object[]
			{
				workItem.Fields.ContainsKey("System.AssignedTo") ? workItem.Fields["System.AssignedTo"] : null,
				workItem.Fields.ContainsKey("System.IterationPath") ? workItem.Fields["System.IterationPath"] : null,
			};

			tags = tags.Where(x => x != null).ToArray();

			if (tags.Length == 0)
			{
				return null;
			}

			return new JsonPatchOperation
			{
				Operation = Operation.Add,
				Path = "/fields/System.Tags",
				Value = string.Join(";", tags)
			};
		}

		private async Task<WorkItem> FindWorkItemByTitleAsync(WorkItemTrackingHttpClient client, string title, string type)
		{
			var query = $"SELECT {ID} FROM WorkItems WHERE ([{TITLE}] = '{title}') and ([{TYPE}] = '{type}')";

			var results = await QueryWorkItemsAsync(client, query);

			return results.Single();
		}

		#endregion
	}
}
