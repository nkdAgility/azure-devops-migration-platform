// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.ProjectLifecycle;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Infrastructure.Agent.ProjectLifecycle;
using DevOpsMigrationPlatform.Infrastructure.Simulated.ProjectLifecycle;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.ProjectLifecycle;

[Binding]
[TestCategory("SystemTest_Simulated")]
public sealed class ProjectLifecycleSteps
{
    private readonly ProjectLifecycleScenarioContext _scenario;
    private readonly ProjectLifecycleService _lifecycleService;

    public ProjectLifecycleSteps(ProjectLifecycleScenarioContext scenario)
    {
        _scenario = scenario;
        _lifecycleService = new ProjectLifecycleService(
            new ProjectLifecycleNameGenerator(),
            new SimulatedProjectLifecycleProvider(),
            NullLogger<ProjectLifecycleService>.Instance);
    }

    [Given(@"a lifecycle-eligible test run for connector ""([^""]*)""")]
    public void GivenALifecycleEligibleTestRunForConnector(string connector)
    {
        _scenario.Context = new ProjectLifecycleContext
        {
            RunId = Guid.NewGuid().ToString("N"),
            ConnectorType = connector,
            ProjectName = string.Empty,
            NamePrefix = "bdd",
            Endpoint = new OrganisationEndpoint { Type = connector, ResolvedUrl = "https://example.test" }
        };
    }

    [When("lifecycle setup executes")]
    public async Task WhenLifecycleSetupExecutes()
    {
        _scenario.Record = await _lifecycleService.CreateAsync(_scenario.Context!);
    }

    [When("lifecycle teardown executes")]
    public async Task WhenLifecycleTeardownExecutes()
    {
        _scenario.TeardownAttempted = true;
        if (_scenario.Record is null && _scenario.Context is not null)
            _scenario.Record = await _lifecycleService.CreateAsync(_scenario.Context);
        if (_scenario.Record is null)
            return;

        _scenario.Record = await _lifecycleService.TeardownAsync(_scenario.Record);
    }

    [Then("setup should succeed")]
    public void ThenSetupShouldSucceed()
    {
        Assert.IsNotNull(_scenario.Record);
        Assert.AreEqual(ProjectLifecycleCreateResult.Succeeded, _scenario.Record.CreateResult);
    }

    [Then("teardown should succeed")]
    public void ThenTeardownShouldSucceed()
    {
        Assert.IsNotNull(_scenario.Record);
        Assert.AreEqual(ProjectLifecycleTeardownResult.Succeeded, _scenario.Record.TeardownResult);
    }

    [Given("the test execution fails after setup")]
    public void GivenTheTestExecutionFailsAfterSetup()
    {
        _scenario.SimulatedExecutionFailure = true;
    }

    [Then("teardown should be attempted")]
    public void ThenTeardownShouldBeAttempted()
    {
        Assert.IsTrue(_scenario.TeardownAttempted);
    }

    [Given(@"lifecycle eligibility allows connector ""([^""]*)""")]
    public void GivenLifecycleEligibilityAllowsConnector(string connector)
    {
        _scenario.Eligibility = new LifecycleEligibilityFlag
        {
            IsEnabled = true,
            Connectors = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { connector }
        };
    }

    [Then(@"lifecycle should be eligible for ""([^""]*)""")]
    public void ThenLifecycleShouldBeEligibleFor(string connector)
    {
        Assert.IsTrue(_scenario.Eligibility.IsEligibleForConnector(connector));
    }
}
