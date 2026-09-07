// The fleet suite runs its cases in name order: a replica killed by one case
// must not be the replica another case still counts on.
[assembly: TestCaseOrderer("Premise.FleetTests.NameOrderer", "Premise.FleetTests")]
[assembly: CollectionBehavior(DisableTestParallelization = true)]
