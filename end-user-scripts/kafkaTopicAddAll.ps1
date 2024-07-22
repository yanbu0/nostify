# This script will add all found Kafka topics to Kafka if they do not already exist.
# Usage: Run this script in the root folder containing the microservices projects.

# Get all kafka_topics.json files in the current directory and child directories
$JsonFiles = Get-ChildItem -Recurse -Filter kafka_topics.json

# Loop through each file and process the topics
foreach ($JsonFile in $JsonFiles) {
    # Read list of kafka topics into variable
    $Topics = Get-Content $JsonFile.FullName | ConvertFrom-Json

    # Loop through list and create topics in Kafka
    foreach ($Topic in $Topics) {
        confluent local kafka topic create $Topic --if-not-exists
    }
}
