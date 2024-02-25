using Blazored.Toast.Services;
using Dto.Financial;
using Dto.Inventory;
using Dto.TaxSystem;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using WebBlazor.Rx;
using WebBlazor.Services.Contracts;

namespace WebBlazor.Pages
{
    public class SuperComponentBase : ComponentBase, IDisposable
    {
        [Inject]
        protected ITaxService taxService { get; set; } = default!;
        [Inject]
        protected ILookupService lookupService { get; set; } = default!;
        [Inject]
        protected IToastService toastService { get; set; } = default!;
        [Inject]
        protected IInventoryService inventoryService { get; set; } = default!;
        [Inject]
        protected LoaderRx loaderRx { get; set; } = default!;
        [Inject]
        protected LookupRx lookupRx { get; set; } = default!;
        [Inject]
        protected AuthenticationStateProvider authStateProvider { get; set; } = default!;
        [Inject]
        protected NavigationManager navManager { get; set; } = default!;
        protected List<IDisposable> reactiveSubscriptions = new();
        protected virtual void DispatchLoadingEvent(bool loading)
        {
            loaderRx.IsLoading.OnNext(loading);
        }

        public void Dispose() => reactiveSubscriptions.ForEach(sub => sub.Dispose());
    }
}
