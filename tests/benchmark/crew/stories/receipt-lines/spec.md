# Feature Specification: Receipt lines

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Print receipt lines (Priority: P1)

The till prints each order line and the total as fixed-width text: the description on the left, the amount on the right.

1. **Given** the line "Coffee" at 3.5, **When** it is formatted, **Then** it is "Coffee" and "3.50" padded with spaces to 32 characters
2. **Given** the line "Coffee" at 3.5 and a width of 12, **When** it is formatted, **Then** it is `Coffee  3.50`
3. **Given** the line "Chocolate cake" at 4.25 and a width of 12, **When** it is formatted, **Then** the description is cut to fit with "…": `Chocol… 4.25`
4. **Given** a width of 4, **When** a line is formatted, **Then** the request is rejected
5. **Given** the amounts 3.50, 4.25 and 1.00, **When** the total is formatted, **Then** it is "TOTAL" and "8.75" padded to 32 characters

## Requirements *(mandatory)*

- **FR-001**: A line MUST be the description, at least one space and the amount with two decimals (invariant culture), padded to 32 characters unless a width is given
- **FR-002**: A line with a given width MUST be exactly that wide; a description too long to fit MUST be cut and end with "…", keeping one space before the amount
- **FR-003**: A width smaller than the amount plus two characters MUST be rejected with an ArgumentOutOfRangeException
- **FR-004**: The total line MUST be "TOTAL" and the sum of the amounts, formatted as a 32-character line
