# Testing Guidelines

Tests live under `tests/`. Extend those patterns instead of creating a separate test architecture.

## Required coverage for feature work

Use isolated test data in unit tests. 

## Cancellation

Include cancellation tests where cancellation is part of the provider/application contract. A cancellation request should propagate rather than being converted into a generic authentication failure.
