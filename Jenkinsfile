pipeline {
    agent any

    options {
        disableConcurrentBuilds()
        buildDiscarder( logRotator( numToKeepStr: '15') )
    }

    environment {
        libraryImage = "fkafka-application"
    }

    stages {
        stage('Library') {
            steps {
                sh "docker build --pull --no-cache -f tests/Dockerfile -t ${libraryImage} ."
            }
        }

        stage('Check') {
            steps {
                sh "docker container run --rm ${libraryImage}"
            }
        }

        stage('Cleanup') {
            steps {
                script {
                    sh "docker rmi --force `docker images -q ${libraryImage} | uniq`"
                }
            }
        }
    }
}
