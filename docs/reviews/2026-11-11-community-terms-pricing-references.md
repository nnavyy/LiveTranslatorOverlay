# Community rankings, terms, and lifetime pricing

## Design references

| Surface | Reference | Applied in Live Translator Overlay |
| --- | --- | --- |
| Rankings | [Strava segment results](https://support.strava.com/en-us/articles/15401900-how-do-i-view-my-segment-results-on-strava) | Separate personal activity from the public table. Show rank, nickname, audio time or verified contributions, and medals. Offer all-time usage, this month in UTC, and supporter views. A search preserves the original rank. |
| Terms | [Visual Studio Code license](https://code.visualstudio.com/license) | Separate application terms and the privacy notice, give each document readable headings, show the bundled revision, and keep consent actions below the scroll area. The preview uses the existing documents and acceptance receipt. |
| Pricing | [GitHub plans and pricing](https://github.com/pricing) and [OBS Project support](https://obsproject.com/contribute) | Align the two lifetime prices and their benefits. Explain what purchases fund, give donations a separate row, and keep stable translation free. |

## Offers

| Offer | Price | Benefits |
| --- | --- | --- |
| Lifetime Supporter | US$19.99 once | Supporter medal and a personal thank-you from the developer. |
| Lifetime Insider | US$100 once | Insider medal, release archive, beta and approved internal builds, compatible experimental models, priority support, and a personal thank-you. |
| Donation | Buyer chooses the amount | Funds development, testing, and releases. It does not grant a lifetime tier. |

The tiers are independent. Public supporter ranking is separate from usage ranking. The public board uses nicknames and opt-in fields. The full buyer list and payment records belong in the private owner workflow.

## Purchase flows

**Secure checkout is the primary flow.** The buyer picks an offer inside the app and signs in with a verified email. A backend creates a checkout session for that account and the server-defined offer, then opens the provider’s checkout in the default browser. Back in the app, the buyer checks their purchase status. The backend verifies settlement and grants access once, including when the provider delivers a webhook twice or the buyer closes the browser before returning. Payment cards stay with the processor. A return URL alone does not activate a license. Stripe’s fulfillment guide is a reference for this design. [Hosted checkout fulfillment](https://docs.stripe.com/checkout/fulfillment?ui=full-page)

**Developer-assisted purchase is the fallback.** A staging build can send an email code to an existing test account and submit a private pending request for either lifetime offer. The server returns the same request ID on retry and takes no money. The remaining production workflow needs a private buyer/developer inbox, an owner sign-in with MFA, and a way to verify settlement and reconcile refunds before it grants access. See [staging instructions](../../supabase/README.md).

The current Windows and macOS builds are direct downloads. Apple’s App Store purchase service is available through Mac App Store distribution. [Apple distribution comparison](https://developer.apple.com/macos/distribution#features-and-requirements)

## Payment provider setup

The developer currently has a wallet in Telegram and plans to set up Stripe later. The wallet is a potential manual receiving method. Confirm the seller country, the exact wallet product, and whether the provider permits license sales in that setup before offering it to buyers.

### Developer-assisted payments

1. Keep the in-app request preview in staging while the receiving method is under review. The customer selects a lifetime offer and verifies their email. Their request contains the selected offer and a private request ID.
2. Once a permitted method and the private inbox are ready, show the buyer the exact amount, currency or asset, payment destination, and expiration in their request. For a supported wallet, also confirm the exact network and any memo requirement in the wallet interface. Walt’s DeFi Account uses TON and routes supported external-network deposits through a separate provider with its own minimums and review. [Walt DeFi Account deposit instructions](https://help.walt.io/article/666-transactions)
3. Reconcile payments against settled receipts in the receiving account. Check the destination, amount, currency or asset, network when applicable, and the link between the receipt and the authenticated buyer’s request. A screenshot or a transaction ID submitted by itself is insufficient. Keep a unique payment reference per provider and network so retries and duplicate claims cannot grant another license.
4. After the MFA-authenticated owner confirms settlement, record the purchase, grant the matching entitlement, and create the private thank-you task in one database transaction. Keep pending requests visible to the buyer. Handle refunds and corrections as linked records and exercise them in staging before opening sales.

If the seller is based in Indonesia, keep crypto off the license checkout. Bank Indonesia says virtual currencies cannot be used as payment in Indonesia. Determine the local payment method before turning on developer-assisted sales. [Bank Indonesia payment-system guidance](https://www.bi.go.id/en/fungsi-utama/sistem-pembayaran/default.aspx)

### Stripe hosted checkout

1. Open and verify the account using the actual seller country and business details. Confirm the permitted payment methods, settlement currency, and whether it can serve international buyers before creating live prices. Stripe currently describes its Indonesia program as invite-only and lists domestic IDR bank transfers for Indonesia-based accounts. A USD checkout for international buyers requires an account and setup that explicitly support it. [Stripe account setup](https://docs.stripe.com/get-started/account/set-up) · [Indonesia account requirements](https://support.stripe.com/questions/requirements-to-open-a-stripe-account-in-indonesia) · [Indonesia payment methods and currencies](https://support.stripe.com/questions/supported-payment-methods-currencies-and-businesses-for-stripe-accounts-in-indonesia)
2. Use a separate Stripe sandbox and a staging backend. For a sandbox that supports USD, configure two one-time test prices for **US$19.99** and **US$100.00**. The backend maps offer IDs to the approved price IDs and binds the checkout to the signed-in account. Keep credentials and webhook signing secrets in the backend’s secret store. [Stripe sandbox testing](https://docs.stripe.com/testing-use-cases)
3. Verify the webhook signature, checkout session, expected offer and amount, and settled status. Fulfill once per payment even if the browser closes or Stripe retries the event. Test successful, cancelled, delayed, duplicate, and refunded payments. Show the verified status inside the app. [Stripe fulfillment](https://docs.stripe.com/checkout/fulfillment?ui=full-page)
4. Switch to live price IDs and a live webhook only after seller verification and the app’s account, purchase, and refund flows pass staging. Publish the matching purchase and privacy terms before opening either route.

## Release checklist

- Select and verify a merchant account. Set up sandbox prices, receipts, a verified webhook, and payment recovery tests before enabling checkout.
- Complete buyer sign-in, the private inbox, the owner dashboard, the entitlement ledger, and the Insider library. Verify cancellations, repeated requests, delayed payment, refunds, restore on a second device, and both independent medals.
- Update the bundled terms and privacy notice for the actual provider, contact and refund process, account and purchase retention, and opt-in public rankings. Publish a new notice revision when those policies change.
- Verify the online board, checkout, installer, and updater on Windows. Verify them plus system audio and microphone on Intel and Apple Silicon, and complete macOS signing and notarization before the joint public release.

The current preview displays the catalog and payment availability. It does not collect payments. Public accounts and usage synchronization remain undeployed.
