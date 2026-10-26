using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Purchasing;

// -----------------------------------------------------------------------------
// UnityIapEntitlementStore — Unity IAP 5 (com.unity.purchasing) adapter for Entitlements
// -----------------------------------------------------------------------------
// Catalog: the one-time Full Game (non-consumable) + First Principles Plus monthly / yearly
// (auto-renewable subscriptions, one subscription group on the App Store; two subscriptions
// with one base plan each on Google Play so the ids match on both stores).
// iOS: StoreKit 2. Android: Google Play Billing. Editor: Unity IAP fake store.
// Ownership is re-derived from the store on every launch (FetchPurchases + CheckEntitlement
// for the subscriptions), so a reinstall / new device restores automatically.
// -----------------------------------------------------------------------------

public sealed class UnityIapEntitlementStore : IEntitlementStore
{
    static readonly string[] SubscriptionIds =
    {
        LevelAccessPolicy.PlusYearlyProductId,
        LevelAccessPolicy.PlusMonthlyProductId,
    };

    StoreController _controller;
    readonly Dictionary<string, Product> _products = new Dictionary<string, Product>();
    bool _connecting;
    bool _plusSeenThisFetch;

    public string PriceFor(string productId)
    {
        if (productId == null || !_products.TryGetValue(productId, out var p))
            return null;
        var md = p?.metadata;
        return md != null && !string.IsNullOrEmpty(md.localizedPriceString) ? md.localizedPriceString : null;
    }

    public bool IsAvailable(string productId) =>
        productId != null && _products.TryGetValue(productId, out var p) && p != null && p.availableToPurchase;

    public async void Connect()
    {
        if (_connecting)
            return;
        _connecting = true;
        try
        {
            if (_controller == null)
            {
                _controller = UnityIAPServices.StoreController();
                _controller.OnPurchasePending += OnPurchasePending;
                _controller.OnPurchaseConfirmed += OnPurchaseConfirmed;
                _controller.OnPurchaseFailed += OnPurchaseFailed;
                _controller.OnPurchaseDeferred += OnPurchaseDeferred;
                _controller.OnProductsFetched += OnProductsFetched;
                _controller.OnProductsFetchFailed += OnProductsFetchFailed;
                _controller.OnPurchasesFetched += OnPurchasesFetched;
                _controller.OnPurchasesFetchFailed += OnPurchasesFetchFailed;
                _controller.OnStoreDisconnected += OnStoreDisconnected;
                _controller.OnCheckEntitlement += OnCheckEntitlement;
                _controller.OnStoreConnected += () => Debug.Log("Entitlements: store connected.");
            }

            await _controller.Connect();

            _controller.FetchProducts(new List<ProductDefinition>
            {
                new ProductDefinition(LevelAccessPolicy.FullGameProductId, ProductType.NonConsumable),
                new ProductDefinition(LevelAccessPolicy.PlusYearlyProductId, ProductType.Subscription),
                new ProductDefinition(LevelAccessPolicy.PlusMonthlyProductId, ProductType.Subscription),
            });
        }
        catch (Exception e)
        {
            Entitlements.OnStoreUnavailable(e.Message);
        }
        finally
        {
            _connecting = false;
        }
    }

    public void Purchase(string productId)
    {
        if (_controller == null || !IsAvailable(productId))
        {
            Entitlements.ReportPurchase(PurchaseOutcome.Failed, "store_unavailable");
            return;
        }
        _controller.PurchaseProduct(_products[productId]);
    }

    public void Restore(Action<bool, string> onDone)
    {
        if (_controller == null)
        {
            onDone?.Invoke(false, "store_unavailable");
            return;
        }

        // Apple: re-syncs transactions with the App Store (may prompt for Apple ID). Google: re-queries owned purchases.
        _controller.RestoreTransactions((ok, err) =>
        {
            // Restored purchases arrive through OnPurchasesFetched / OnPurchasePending;
            // fetch explicitly so ownership is re-evaluated even if the store did not push them.
            if (ok)
                RefreshOwnership();
            onDone?.Invoke(ok, err);
        });
    }

    void RefreshOwnership()
    {
        if (_controller == null)
            return;
        _plusSeenThisFetch = false;
        _pendingNotEntitled.Clear();
        _controller.FetchPurchases();
        foreach (var id in SubscriptionIds)
        {
            if (_products.TryGetValue(id, out var p) && p != null)
                _controller.CheckEntitlement(p);
        }
    }

    // ------------------------------------------------------------------ callbacks

    void OnProductsFetched(List<Product> products)
    {
        foreach (var p in products)
        {
            if (p?.definition != null)
                _products[p.definition.id] = p;
        }

        bool anyAvailable = false;
        foreach (var kv in _products)
            anyAvailable |= kv.Value != null && kv.Value.availableToPurchase;

        if (!anyAvailable)
            Entitlements.OnStoreUnavailable("no products available");
        else
            Entitlements.OnStoreReady();

        // Receipt / entitlement sync: owned non-consumables + active subscriptions come back as ConfirmedOrders.
        RefreshOwnership();
    }

    void OnProductsFetchFailed(ProductFetchFailed failure)
    {
        // Partial failure still delivers the products that exist (e.g. subscriptions not live yet).
        if (failure?.FailedFetchProducts != null && _products.Count > 0)
        {
            Entitlements.OnStoreReady();
        }
        else
        {
            Entitlements.OnStoreUnavailable(failure?.FailureReason ?? "products fetch failed");
        }
        // Still try to read existing purchases so an owner is never locked out.
        _controller?.FetchPurchases();
    }

    void OnPurchasesFetched(Orders orders)
    {
        if (orders == null)
            return;
        foreach (var o in orders.ConfirmedOrders)
            ApplyOwnedOrder(o);
        foreach (var o in orders.PendingOrders)
        {
            // Interrupted purchase (app killed before confirmation) — finish it now.
            ApplyOwnedOrder(o);
            _controller.ConfirmPurchase(o);
        }
    }

    void OnCheckEntitlement(Entitlement e)
    {
        string id = e?.Product?.definition?.id;
        if (!LevelAccessPolicy.IsPlusProduct(id))
            return;
        switch (e.Status)
        {
            case EntitlementStatus.FullyEntitled:
            case EntitlementStatus.EntitledButNotFinished:
                _plusSeenThisFetch = true;
                Entitlements.GrantPlus(ExpiryFrom(e.Order, id));
                break;
            case EntitlementStatus.NotEntitled:
                // Only revoke once every Plus product has been checked and none is active.
                _pendingNotEntitled.Add(id);
                if (_pendingNotEntitled.Count >= SubscriptionIds.Length && !_plusSeenThisFetch)
                    Entitlements.RevokePlus();
                break;
        }
    }

    readonly HashSet<string> _pendingNotEntitled = new HashSet<string>();

    void OnPurchasesFetchFailed(PurchasesFetchFailureDescription failure)
    {
        Debug.LogWarning($"Entitlements: purchases fetch failed ({failure?.FailureReason}: {failure?.Message}).");
    }

    void OnPurchasePending(PendingOrder order)
    {
        ApplyOwnedOrder(order);
        // Grant first, then acknowledge (Google requires acknowledgement within 3 days).
        _controller.ConfirmPurchase(order);
    }

    void OnPurchaseConfirmed(Order order)
    {
        switch (order)
        {
            case ConfirmedOrder confirmed:
                if (ApplyOwnedOrder(confirmed))
                    Entitlements.ReportPurchase(PurchaseOutcome.Succeeded, null);
                break;
            case FailedOrder failed:
                Entitlements.ReportPurchase(OutcomeFor(failed.FailureReason), failed.Details);
                break;
        }
    }

    void OnPurchaseFailed(FailedOrder failed)
    {
        if (failed == null)
            return;
        // Already owned (e.g. bought on another device, cache wiped): treat as success after a fetch.
        if (failed.FailureReason == PurchaseFailureReason.DuplicateTransaction)
        {
            RefreshOwnership();
            Entitlements.ReportPurchase(PurchaseOutcome.Failed, "already_owned_restore");
            return;
        }
        Entitlements.ReportPurchase(OutcomeFor(failed.FailureReason), failed.Details);
    }

    void OnPurchaseDeferred(DeferredOrder order)
    {
        Entitlements.ReportPurchase(PurchaseOutcome.Deferred, null);
    }

    void OnStoreDisconnected(StoreConnectionFailureDescription failure)
    {
        Entitlements.OnStoreUnavailable(failure?.Message ?? "disconnected");
    }

    static PurchaseOutcome OutcomeFor(PurchaseFailureReason reason) =>
        reason == PurchaseFailureReason.UserCancelled ? PurchaseOutcome.Cancelled : PurchaseOutcome.Failed;

    /// <summary>Grants whatever the order contains. Returns true if it held a catalog product.</summary>
    bool ApplyOwnedOrder(Order order)
    {
        var items = order?.CartOrdered?.Items();
        if (items == null)
            return false;
        bool any = false;
        foreach (var item in items)
        {
            string id = item?.Product?.definition?.id;
            if (id == LevelAccessPolicy.FullGameProductId)
            {
                Entitlements.GrantFullGame();
                any = true;
            }
            else if (LevelAccessPolicy.IsPlusProduct(id))
            {
                var info = SubscriptionInfoFor(order, id);
                // Expired / cancelled-and-lapsed subscriptions can still show up in history on some stores.
                if (info != null && (info.IsExpired() == Result.True || info.IsSubscribed() == Result.False))
                    continue;
                _plusSeenThisFetch = true;
                Entitlements.GrantPlus(ExpiryFrom(order, id));
                any = true;
            }
        }
        return any;
    }

    static SubscriptionInfo SubscriptionInfoFor(Order order, string productId)
    {
        try
        {
            var infos = order?.Info?.PurchasedProductInfo;
            if (infos == null)
                return null;
            foreach (var i in infos)
                if (i != null && i.productId == productId)
                    return i.subscriptionInfo;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Entitlements: subscription info unavailable ({e.Message}).");
        }
        return null;
    }

    static DateTime? ExpiryFrom(Order order, string productId)
    {
        var info = SubscriptionInfoFor(order, productId);
        if (info == null)
            return null;
        try
        {
            var d = info.GetExpireDate();
            return d > DateTime.UtcNow ? d : (DateTime?)null;
        }
        catch
        {
            return null;
        }
    }
}
