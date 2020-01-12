
pipeline {
    agent { label 'AQA' }
	environment { 
        executeScriptsDir = './oms-jenkins-deployment/execute-scripts'
    }
    parameters {
        booleanParam(name: 'Step1', defaultValue: true, description: 'Deploy AWS Environment')
        booleanParam(name: 'Step2', defaultValue: true, description: 'Deploy OMS')
        booleanParam(name: 'Step3', defaultValue: true, description: 'Set Install Parameters')
        booleanParam(name: 'Step4', defaultValue: true, description: 'Deploy Algos')
        booleanParam(name: 'Step5', defaultValue: true, description: 'Deploy Holiday Calendar')
		booleanParam(name: 'Step6', defaultValue: true, description: 'Configure OMS for AQA Environment')
        booleanParam(name: 'Step7', defaultValue: true, description: 'Email Deployment Complete')
        booleanParam(name: 'Step8', defaultValue: true, description: 'Clear Nuget Packages')
        
        string(name: 'manualAdministratorPassword', defaultValue: 'none', description: 'Machine administrator password')
        string(name: 'buildOriginal', defaultValue: 'latest', description: 'Build to deploy OMS on (if specific)')
        string(name: 'databaseHostnameOriginal', defaultValue: 'computed', description: 'Target database hostname')
    }
   options { 
        timestamps () 
        buildDiscarder(logRotator(numToKeepStr: '15', artifactNumToKeepStr: '15'))
   }

   stages {
		stage('Clone Source Code') {
			steps{
				//Clones specified git repos to local workstation
				dir('oms-jenkins-deployment') {
					git branch: 'master', credentialsId: '8bd8524e-19fe-4e12-b5fa-850b1c2f03fe', url: 'ssh://git@stash.ezesoft.net:7999/caqa/oms-jenkins-deployment.git'
				}
				dir('jenkins-deployment-config') {
    			    git branch: 'master', credentialsId: '8bd8524e-19fe-4e12-b5fa-850b1c2f03fe', url: 'ssh://git@stash.ezesoft.net:7999/caqa/jenkins-deployment-config.git'
				}
				//Import the environment variables used for this specified environment
				script{
					def CommonModule = load("Common.groovy");
					CommonModule.ImportPropertiesFile(env.PropertiesFilePath);
				}
			}
		}        

        //If executed, will trigger a job to delete the old AWS enviroment with the same name (if found) and redeploy a new one through the provisoning service
		stage('Deploy AWS Environment') {
            when { equals expected: true, actual: params.Step1 }
			steps {
                build job: 'Components/Deploy_AWS_Environment', 
                parameters: [
                    string(name: 'buildOriginal', value: env.buildOriginal),
                    string(name: 'databaseHostnameOriginal', value: env.databaseHostnameOriginal),
                    string(name: 'PropertiesFilePath', value: env.PropertiesFilePath)
                ], 
                wait: true;
                
                //This contains the administrator password allocated to machines within this AWS stack
                copyArtifacts projectName: 'Components/Deploy_AWS_Environment', selector: lastSuccessful();
            }
		}
		
		//This will execute if the job IS NOT provided an administrator password. Generally, this executes on first run of the job
		stage('Inject Administrator Password - Dynamic') {
		    when { expression {env.manualAdministratorPassword == "none" } }
			steps {
                //No manual admin password was provided, use the one dynamically created
                script{
					def CommonModule = load("Common.groovy");
					CommonModule.ImportPropertiesFile("./env.properties");
				}
            }
		}
		
		//This will execute if the job IS provided an administrator password. Generally, this executes on subsequent runs of this job on the same environment stack
		stage('Inject Administrator Password - Manual') {
            when { expression { env.manualAdministratorPassword != "none" } }
			steps {
                //User provided a manual password, use this one
                writeFile file: 'env.properties', text: 'administratorPassword=' + env.manualAdministratorPassword
				script{
					def CommonModule = load("Common.groovy");
					CommonModule.ImportPropertiesFile("./env.properties");
				}
                print "Using user provided password: " + administratorPassword;
			}
		}
		
		//If executed, will trigger a job to deploy the OMS databse and server installs to targeted AWS environments
		stage('Deploy OMS') {
            when { expression {(params.Step2  == true) && (env.pipeline_DeployOmsStack == '1')} }
			steps {
                build job: 'Components/Deploy_OMS', 
                parameters: [
                    string(name: 'administratorPassword', value: env.administratorPassword),
                    string(name: 'PropertiesFilePath', value: env.PropertiesFilePath)
                ], 
                wait: true;
            }
		}
		
		//If executed, will import the environment details as Jenkins parmaters. These include:
		//  1. OMS installed Build Number
		//  2. TC database hostname 
		//  3. installed hostfix release (if applicable)
		stage('Set Install Parameters') {
            when { equals expected: true, actual: params.Step3 }
			steps {
                script {
                    powershell script: "$executeScriptsDir/Execute-Set-TeamcityParameters.ps1;"
					def CommonModule = load("Common.groovy");
					CommonModule.ImportPropertiesFile("./env.properties");
                }
			}
		}
		
		//If executed, will trigger a job to deploy the Algo components to targeted AWS environments
		stage('Deploy Algos') {
            when { expression {(params.Step4  == true) && (env.pipeline_DeployAlgosStack == '1')} }
			steps {
                build job: 'Components/Deploy_Algos',  
                parameters: [
                    string(name: 'PropertiesFilePath', value: env.PropertiesFilePath)
                ],
                wait: true;
            }
		}	
		
		//If executed, will trigger a job to deploy the OMS Holiday Calendar to the targeted AWS database host
		stage('Deploy Holiday Calendar') {
            when { expression {(params.Step5  == true) && (env.pipeline_DeployOMSHolidayCalendar == '1')} }
			steps {
                build job: 'Components/Deploy_HolidayCalendar',  
                parameters: [
                    string(name: 'PropertiesFilePath', value: env.PropertiesFilePath)
                ],
                wait: true;
            }
		}
		
		stage('OctopusDeployment Configure OMS For AQA Testing') {
		    when { expression {(params.Step6  == true) && (env.pipeline_DeployOmsStack == '1')} }
			steps {
                powershell script: "$executeScriptsDir/Execute-OctopusDeployment-ConfigureOMSForAQAPipeline.ps1;"
			}
		}
		
		//If executed, emails a deployment success email with environment details
		stage('Email Deployment Completion') {
            when { equals expected: true, actual: params.Step7 }
			steps {
                powershell script: "$executeScriptsDir/Execute-Email-DeploymentCompletion.ps1;"
            }
		}
		
		//If executed, declutters the Jenkins Agent of any downloaded nuget packages
		stage('Clear Nuget Packages') {
			when { equals expected: true, actual: params.Step8 }
			steps {
                powershell script: "$executeScriptsDir/Execute-Clear-NugetPackages.ps1;"
			}
		}
    }
	post {
		success {
            archiveArtifacts artifacts: 'env.properties', allowEmptyArchive: true, fingerprint: true
            
            build job: 'Execute_Tests',  
            parameters: [
                string(name: 'administratorPassword', value: env.administratorPassword),
				string(name: 'PropertiesFilePath', value: env.PropertiesFilePath)
            ],
            wait: false;
        }
        failure {
            emailext subject: "$environmentName Pipeline Failed",
				body: "$environmentName has failed, please see attached build log for more info.",
				to: "$deploymentEmailRecipients",
				from: 'eze.oms.autoqa@ezesoft.com',
				attachLog: true;
     
            slackSend channel: '#qa-portal', color: "danger", message: "$environmentName has failed, please see build log for more info: <${env.JOB_URL}${env.build_Id}/console|Build Log>";
        }
	}
}