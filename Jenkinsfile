pipeline {
    agent {
        kubernetes {
            yaml '''
apiVersion: v1
kind: Pod
metadata:
  labels:
    jenkins: backend-ci
spec:
  containers:
  - name: dotnet
    image: mcr.microsoft.com/dotnet/sdk:10.0
    command:
    - sleep
    args:
    - 99d
  - name: kaniko
    image: gcr.io/kaniko-project/executor:debug
    command:
    - sleep
    args:
    - 99d
    volumeMounts:
    - name: ghcr-config
      mountPath: /kaniko/.docker
  volumes:
  - name: ghcr-config
    secret:
      secretName: ghcr-push-secret
      items:
      - key: .dockerconfigjson
        path: config.json
'''
        }
    }

    triggers {
        // Poll SCM every 5 minutes for new commits
        pollSCM('H/5 * * * *')
    }

    environment {
        // NOTE: GitHub Container Registry requires image names to be lowercase
        IMAGE_NAME = 'banking-api'
        // Replace with your GitHub username or organization (lowercase)
        GHCR_OWNER = 'nhahub'
        IMAGE_REPO = "ghcr.io/${GHCR_OWNER}/${IMAGE_NAME}"
    }

    stages {
        stage('Restore & Build') {
            steps {
                container('dotnet') {
                    echo "=== Restoring and Building Backend (.NET 10) ==="
                    sh 'dotnet restore Backend/BankingApi.sln'
                    sh 'dotnet build Backend/BankingApi.sln --no-restore -c Release /p:TreatWarningsAsErrors=true'
                }
            }
        }

        stage('Run Tests') {
            steps {
                container('dotnet') {
                    echo "=== Running Integration & Unit Tests ==="
                    sh 'dotnet test Backend/BankingApi.sln --no-build -c Release --nologo -v quiet --logger "console;verbosity=minimal"'
                }
            }
        }

        stage('Build & Push to GHCR') {
            steps {
                container('kaniko') {
                    echo "=== Building and Pushing Container to GHCR using Kaniko ==="
                    sh """
                    /kaniko/executor \
                        --context=dir://. \
                        --dockerfile=Dockerfile \
                        --destination=${IMAGE_REPO}:${BUILD_NUMBER} \
                        --destination=${IMAGE_REPO}:latest \
                        --cache=true
                    """
                }
            }
        }
    }

    post {
        success {
            echo "CI Pipeline completed successfully! Image pushed to: ${IMAGE_REPO}:${BUILD_NUMBER}"
        }
        failure {
            echo "CI Pipeline failed. Check the stage logs above."
        }
    }
}
