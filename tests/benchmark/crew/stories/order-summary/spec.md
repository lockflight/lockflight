# Feature Specification: Order summary

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Summarize an order (Priority: P1)

Checkout shows the number of items, the subtotal and the most expensive line of an order.

1. **Given** lines 2 × 8.50 (`SKU-1`) and 1 × 20.00 (`SKU-2`), **When** the order is summarized, **Then** it has 3 items, a subtotal of 37.00 and `SKU-2` as its most expensive line
2. **Given** an order with no lines, **When** it is summarized, **Then** it has 0 items, a subtotal of 0.00 and no most expensive line
3. **Given** two lines of the same line total, **When** the order is summarized, **Then** the first of them is the most expensive line
4. **Given** a line with a quantity of zero, **When** the order is summarized, **Then** the request is rejected

## Requirements *(mandatory)*

- **FR-001**: The summary MUST give the total number of items and the subtotal (sum of quantity × unit price)
- **FR-002**: The summary MUST name the SKU of the line with the highest line total, the first one on a tie, and none for an empty order
- **FR-003**: A line with a quantity of zero or less MUST be rejected with an ArgumentOutOfRangeException
- **FR-004**: Summarizing MUST be asynchronous and honour cancellation
