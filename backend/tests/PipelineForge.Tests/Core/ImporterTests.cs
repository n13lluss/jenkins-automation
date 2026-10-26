using PipelineForge.Core.Generation;
using PipelineForge.Core.Importing;
using PipelineForge.Core.Model;

namespace PipelineForge.Tests.Core;

public class ImporterTests
{
    private const string Legacy = """
        pipeline {
            agent { label 'linux' }
            options { disableConcurrentBuilds() }
            stages {
                stage('Build') {
                    steps {
                        sh 'mvn -B clean verify'
                    }
                }
                stage('Sonar') {
                    steps {
                        withSonarQubeEnv('corp-sonar') { sh 'mvn sonar:sonar -Dsonar.projectKey=corp_orders' }
                    }
                }
                stage('Notify') {
                    steps {
                        slackSend channel: '#builds', message: "Built ${env.BUILD_NUMBER}" // keep { braces } in comments
                    }
                }
                stage('Deploy to production') {
                    when { branch 'master' }
                    steps {
                        input message: 'Ship it?', submitter: 'ops'
                        withCredentials([file(credentialsId: 'prod-kube', variable: 'KUBECONFIG')]) {
                            sh 'helm upgrade --install orders chart/'
                        }
                    }
                }
            }
        }
        """;

    [Fact]
    public void Maps_known_stages_and_keeps_unknown_ones()
    {
        var result = JenkinsfileImporter.Import(Legacy, "orders");
        var c = result.Config;
        Assert.Equal(AgentType.Docker, c.Agent.Type);
        Assert.Equal("linux", c.Agent.DockerLabel);
        Assert.Equal(Ecosystem.Maven, Assert.Single(c.Modules).Ecosystem);
        Assert.True(c.Quality.Enabled);
        Assert.Equal("corp-sonar", c.Quality.ServerName);
        Assert.Equal("corp_orders", c.Quality.ProjectKey);
        var prod = Assert.Single(c.Environments);
        Assert.Equal("prod", prod.Key);
        Assert.Equal("master", prod.BranchPattern);
        Assert.True(prod.RequiresApproval);
        Assert.Equal(DeployTargetType.Helm, prod.Deploy.Type);
        var custom = Assert.Single(c.CustomStages);
        Assert.Equal("Notify", custom.Name);
        Assert.StartsWith("slackSend", custom.Steps);
        Assert.Contains("prod-kube", result.CredentialIds);
        Assert.Contains(result.Gaps, g => g.Contains("secret scanning"));
        Assert.Contains(result.Gaps, g => g.Contains("quality gate"));
    }

    [Fact]
    public void Imported_config_generates_after_the_gaps_are_filled()
    {
        var c = JenkinsfileImporter.Import(Legacy, "orders").Config;
        c.Modules[0].Dockerfile = "Dockerfile";
        c.Modules[0].ImageName = "corp/orders";
        c.Registry = new RegistryConfig { Host = "registry.corp.example" };
        var result = PipelineGenerator.Generate(c);
        Assert.True(result.Success, string.Join("\n", result.Errors));
        Assert.Contains("stage('Notify')", result.Files.Single(f => f.Path == "Jenkinsfile").Content);
    }

    [Fact]
    public void Scripted_pipelines_are_kept_as_one_custom_stage()
    {
        var result = JenkinsfileImporter.Import("node { sh 'make' }");
        Assert.Single(result.Config.CustomStages);
        Assert.NotEmpty(result.Warnings);
    }
}
