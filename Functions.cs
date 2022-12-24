using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Compute;
using System.Linq;
using System;

namespace ArtworkInbox.VirtualMachineManagement
{
    public static class Functions
    {
        public static VirtualMachineResource VirtualMachineResource {
            get {
                var credential = new ChainedTokenCredential(
                    new ManagedIdentityCredential(),
                    new VisualStudioCredential(new VisualStudioCredentialOptions { TenantId = "dd259809-e6e5-487a-bdfb-8bf0a973b11e" }));
                var client = new ArmClient(credential);
                return client.GetVirtualMachineResource(new Azure.Core.ResourceIdentifier("/subscriptions/533a2be0-3b41-4141-a2cd-a97d9dc4c201/resourcegroups/artworkinbox/providers/Microsoft.Compute/virtualMachines/artworkinbox-db"));
            }
        }

        public static async Task<IReadOnlyList<string>> GetPowerStatesAsync() {
            var instanceView = await VirtualMachineResource.InstanceViewAsync();
            return instanceView.Value.Statuses.Select(x => x.Code).ToList();
        }

        [FunctionName("power-states")]
        public static async Task<IActionResult> PowerStates([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequest req) {
            return new OkObjectResult(await GetPowerStatesAsync());
        }

        [FunctionName("start")]
        public static async Task Start([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req)
        {
            if (!(await GetPowerStatesAsync()).Contains("PowerState/running"))
                await VirtualMachineResource.PowerOnAsync(Azure.WaitUntil.Completed);

            await VirtualMachineResource.AddTagAsync("ArtworkInboxShutdownAt", $"{DateTimeOffset.UtcNow.AddHours(2):o}");
        }

        [FunctionName("stop")]
        public static async Task StopAsync([TimerTrigger("0 35 * * * *")] TimerInfo myTimer) {
            var tags = await VirtualMachineResource.GetTagResource().GetAsync();
            if (tags.Value.Data.TagValues.TryGetValue("ArtworkInboxShutdownAt", out string str) && DateTimeOffset.TryParse(str, out DateTimeOffset dt) && dt < DateTimeOffset.UtcNow) {
                await VirtualMachineResource.DeallocateAsync(Azure.WaitUntil.Completed);
                await VirtualMachineResource.RemoveTagAsync("ArtworkInboxShutdownAt");
            }
        }
    }
}
