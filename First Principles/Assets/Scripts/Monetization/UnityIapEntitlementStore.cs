using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Purchasing;

// -----------------------------------------------------------------------------
// UnityIapEntitlementStore — Unity IAP 5 (com.unity.purchasing) adapter for Entitlements
// -----------------------------------------------------------------------------
// iOS: StoreKit 2 (transactions are JWS-verified by StoreKit on device).
// Android: Google Play Billing. Editor: Unity IAP fake store (simulated purchase dialog).
// Ownership is re-derived from the store on every launch via FetchPurchases, so a
// reinstall / new device restores automatically; Restore Purchases is the manual path.
// -----------------------------------------------------------------------------

public sealed class UnityIapEntitlementStore : IEntitlementStore
{
    StoreController _controller;
    Product _product;
    bool _connecting;

    public string LocalizedPrice
    {
        get
        {
            var md = _product?.metadata;
            return md != null && !string.IsNullOrEmpty(md.localizedPriceString) ? md.localizedPriceString : null;
        }
    }

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
                _controller.OnStoreConnected += () => Debug.Log("Entitlements: store connected.");
            }

            await _controller.Connect();

            _controller.FetchProducts(new List<ProductDefinition>
            {
                new ProductDefinition(LevelAccessPolicy.FullGameProductId, ProductType.NonConsumable),
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

    public void PurchaseFullGame()
    {
        if (_controller == null || _product == null)
        {
            Entitlements.ReportPurchase(PurchaseOutcome.Failed, "store_unavailable");
            return;
        }
        _controller.PurchaseProduct(_product);
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
            // Restored non-consumables arrive through OnPurchasesFetched / OnPurchasePending;
            // fetch explicitly so ownership is re-evaluated even if the store did not push them.
            if (ok)
                _controller.FetchPurchases();
            onDone?.Invoke(ok, err);
        });
    }

    // ------------------------------------------------------------------ callbacks

    void OnProductsFetched(List<Product> products)
    {
        foreach (var p in products)
        {
            if (p?.definition != null && p.definition.id == LevelAccessPolicy.FullGameProductId)
                _product = p;
        }

        if (_product == null || !_product.availableToPurchase)
        {
            Entitlements.OnStoreUnavailable($"product '{LevelAccessPolicy.FullGameProductId}' not available");
        }
        else
        {
            Entitlements.OnStoreReady();
        }

        // Receipt / entitlement sync: owned non-consumables come back as ConfirmedOrders.
        _controller.FetchPurchases();
    }

    void OnProductsFetchFailed(ProductFetchFailed failure)
    {
        Entitlements.OnStoreUnavailable(failure?.FailureReason ?? "products fetch failed");
        // Still try to read existing purchases so an owner is never locked out.
        _controller?.FetchPurchases();
    }

    void OnPurchasesFetched(Orders orders)
    {
        if (orders == null)
            return;
        foreach (var o in orders.ConfirmedOrders)
        {
            if (ContainsFullGame(o))
                Entitlements.GrantFullGame();
        }
        foreach (var o in orders.PendingOrders)
        {
            // Interrupted purchase (app killed before confirmation) — finish it now.
            if (ContainsFullGame(o))
            {
                Entitlements.GrantFullGame();
                _controller.ConfirmPurchase(o);
            }
        }
    }

    void OnPurchasesFetchFailed(PurchasesFetchFailureDescription failure)
    {
        Debug.LogWarning($"Entitlements: purchases fetch failed ({failure?.FailureReason}: {failure?.Message}).");
    }

    void OnPurchasePending(PendingOrder order)
    {
        if (ContainsFullGame(order))
            Entitlements.GrantFullGame();
        // Non-consumable: grant first, then acknowledge (Google requires acknowledgement within 3 days).
        _controller.ConfirmPurchase(order);
    }

    void OnPurchaseConfirmed(Order order)
    {
        switch (order)
        {
            case ConfirmedOrder confirmed when ContainsFullGame(confirmed):
                Entitlements.GrantFullGame();
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
            _controller.FetchPurchases();
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

    static bool ContainsFullGame(Order order)
    {
        var items = order?.CartOrdered?.Items();
        if (items == null)
            return false;
        foreach (var item in items)
        {
            if (item?.Product?.definition?.id == LevelAccessPolicy.FullGameProductId)
                return true;
        }
        return false;
    }
}
