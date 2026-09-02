using Xunit;

// Order-independence guard: opt-in shuffling via PREMISE_TEST_SHUFFLE=<seed>.
// See RandomOrderer.
[assembly: TestCaseOrderer("Premise.IntegrationTests.RandomOrderer", "Premise.IntegrationTests")]
