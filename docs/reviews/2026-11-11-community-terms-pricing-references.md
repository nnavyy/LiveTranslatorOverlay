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

## Release checklist

- Select and verify a merchant account. Set up sandbox prices, receipts, a verified webhook, and payment recovery tests before enabling checkout.
- Complete buyer sign-in, the private inbox, the owner dashboard, the entitlement ledger, and the Insider library. Verify cancellations, repeated requests, delayed payment, refunds, restore on a second device, and both independent medals.
- Update the bundled terms and privacy notice for the actual provider, contact and refund process, account and purchase retention, and opt-in public rankings. Publish a new notice revision when those policies change.
- Verify the online board, checkout, installer, and updater on Windows. Verify them plus system audio and microphone on Intel and Apple Silicon, and complete macOS signing and notarization before the joint public release.

The current preview displays the catalog and payment availability. It does not collect payments. Public accounts and usage synchronization remain undeployed.
