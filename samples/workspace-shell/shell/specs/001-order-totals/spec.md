# Feature Specification: Order totals

**Feature Branch**: `001-order-totals`
**Created**: 2026-02-03
**Status**: Implemented
**Services**: services/orders

## User Scenarios & Testing

### User Story 1 - See the order total (Priority: P1)

A customer sees the total of an order, including every line.

**Acceptance Scenarios**:

1. **Given** an order with two lines, **When** the total is requested, **Then** it is the sum of quantity × unit price.

## Requirements

- **FR-001**: The total of an order MUST be the sum of its lines' quantity × unit price.
- **FR-002**: An order without lines MUST have a total of zero.

## Success Criteria

- **SC-001**: Totals match the invoice for 100% of orders.
