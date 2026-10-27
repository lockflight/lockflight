# Feature Specification: Gift cards

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Redeem a gift card (Priority: P1)

At checkout a customer pays part or all of an order with a gift card.

1. **Given** a card with a balance of 50.00 EUR that expires on 2026-12-31, **When** 20.00 EUR is redeemed on 2026-10-27, **Then** the card's balance is 30.00 EUR
2. **Given** a card with a balance of 15.00 EUR, **When** 20.00 EUR is redeemed, **Then** the request is rejected and nothing is redeemed
3. **Given** a card that expired on 2026-10-26, **When** it is redeemed on 2026-10-27, **Then** the request is rejected
4. **Given** a card that expires on 2026-10-27, **When** it is redeemed on 2026-10-27, **Then** the redemption succeeds
5. **Given** a redemption of 0.00 EUR or of -5.00 EUR, **When** it is redeemed, **Then** the request is rejected
6. **Given** a card in EUR, **When** 10.00 USD is redeemed, **Then** the request is rejected

## Requirements *(mandatory)*

- **FR-001**: Redeeming MUST return the card with its balance reduced by the amount
- **FR-002**: Redeeming more than the balance MUST be rejected with an InvalidOperationException
- **FR-003**: A card MUST be redeemable up to and including its expiry date and rejected with an InvalidOperationException after it
- **FR-004**: A redemption amount of zero or less MUST be rejected with an ArgumentOutOfRangeException
- **FR-005**: A redemption in another currency than the card's MUST be rejected with an ArgumentException
