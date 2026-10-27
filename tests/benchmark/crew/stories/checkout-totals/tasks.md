# Tasks: Checkout totals

## Phase 3: User Story 1 - Total a cart at checkout (Priority: P1)

- [x] T010 [US1] (FR-001, FR-002, FR-003, FR-004, FR-005, FR-006, FR-007, FR-008) Declare TaxCategory, CartItem, CheckoutTotal and the TaxCalculator, ShippingQuote, PromoCodes and CheckoutCalculator stubs in src/Shop.Api/Checkout/

### Tests for User Story 1 (MANDATORY — write first, ensure they FAIL) ⚠️

- [ ] T011 [US1] (FR-001, FR-002) Tax tests in tests/Shop.Tests/Checkout/TaxCalculatorTests.cs
- [ ] T012 [US1] (FR-003, FR-004) Shipping tests in tests/Shop.Tests/Checkout/ShippingQuoteTests.cs
- [ ] T013 [US1] (FR-005) Promo code tests in tests/Shop.Tests/Checkout/PromoCodesTests.cs
- [ ] T014 [US1] (FR-006, FR-007, FR-008) Checkout total tests in tests/Shop.Tests/Checkout/CheckoutCalculatorTests.cs

### Implementation for User Story 1

- [ ] T015 [US1] (FR-001, FR-002) Implement TaxCalculator.TaxFor
- [ ] T016 [US1] (FR-003, FR-004) Implement ShippingQuote.CostFor
- [ ] T017 [US1] (FR-005) Implement PromoCodes.TryParse
- [ ] T018 [US1] (FR-006, FR-007, FR-008) Implement CheckoutCalculator.Total
