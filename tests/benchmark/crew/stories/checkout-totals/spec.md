# Feature Specification: Checkout totals

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Total a cart at checkout (Priority: P1)

Checkout shows the net amount, the discount, the tax, the shipping and the grand total of a cart.

1. **Given** a net amount of 10.00 in the standard category, **When** tax is computed, **Then** it is 2.00
2. **Given** a net amount of 10.10 in the food category, **When** tax is computed, **Then** it is 0.50 (0.505 rounded half to even)
3. **Given** a subtotal of 59.99, **When** shipping is quoted, **Then** it costs 4.90
4. **Given** a subtotal of 60.00, **When** shipping is quoted, **Then** it is free
5. **Given** the promo code `SAVE15`, **When** it is parsed, **Then** it is valid for 15 percent
6. **Given** the promo codes `save15`, `SAVE`, `SAVE100` and `FREE10`, **When** each is parsed, **Then** none is valid
7. **Given** one standard item of 2 × 25.00 and the code `SAVE10`, **When** the cart is totalled, **Then** net is 50.00, discount 5.00, tax 9.00, shipping 4.90 and the grand total 58.90
8. **Given** one food item of 1 × 80.00 and no code, **When** the cart is totalled, **Then** net is 80.00, discount 0.00, tax 4.00, shipping 0.00 and the grand total 84.00
9. **Given** an empty cart, **When** it is totalled, **Then** the request is rejected

## Requirements *(mandatory)*

- **FR-001**: Tax in the standard category MUST be 20 percent of the net amount, rounded to cents half to even
- **FR-002**: Tax in the food category MUST be 5 percent of the net amount, rounded to cents half to even
- **FR-003**: Shipping MUST cost 4.90 for a subtotal below 60.00
- **FR-004**: Shipping MUST be free for a subtotal of 60.00 or more
- **FR-005**: A promo code MUST be `SAVE` followed by one or two digits (upper case) and give that percentage; any other code is invalid
- **FR-006**: A promo discount MUST apply to the net amount before tax, and tax and shipping MUST be computed on the discounted amount
- **FR-007**: The grand total MUST be net minus discount plus tax plus shipping
- **FR-008**: Totalling an empty cart MUST be rejected with an ArgumentException
