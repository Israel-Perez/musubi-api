using Musubi.TestCoverage;

// SCRUM-120: al terminar la suite escribe TestResults/endpoint-coverage/<Suite>.txt (scripts/check_endpoint_tests.ps1).
[assembly: AssemblyFixture(typeof(EndpointCoverage))]
