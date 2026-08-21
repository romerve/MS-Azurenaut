#!/bin/zsh
emulate -LR zsh
setopt errexit nounset pipefail

readonly ROOT="${0:A:h:h}"
export WORKSHOP_ROOT="$ROOT"

ruby <<'RUBY'
require "yaml"

root = ENV.fetch("WORKSHOP_ROOT")
workflow_dir = File.join(root, ".github", "workflows")
required = %w[
  challenge-broken.yml
  ci.yml
  deploy-existing-container-app.yml
  dependency-review.yml
  codeql.yml
  publish-supply-chain.yml
]
errors = []

required.each do |name|
  path = File.join(workflow_dir, name)
  unless File.file?(path)
    errors << "missing workflow: #{name}"
    next
  end
  begin
    YAML.safe_load(File.read(path), aliases: true)
  rescue Psych::SyntaxError => error
    errors << "#{name}: YAML parse failed: #{error.message.lines.first.strip}"
  end
end

read = ->(name) { File.read(File.join(workflow_dir, name)) }
challenge = read.call("challenge-broken.yml")
ci = read.call("ci.yml")
deploy = read.call("deploy-existing-container-app.yml")
dependency = read.call("dependency-review.yml")
codeql = read.call("codeql.yml")
supply = read.call("publish-supply-chain.yml")
all = required.map { |name| read.call(name) }.join("\n")

errors << "challenge must contain the intentional Release.API path defect" unless challenge.include?("src/Release.API/Release.API.csproj")
errors << "challenge unexpectedly uses the corrected project path" if challenge.include?("src/Release.Api/Release.Api.csproj")
errors << "corrected CI does not build the API project" unless ci.include?("src/Release.Api/Release.Api.csproj")
errors << "corrected CI does not build the solution" unless ci.include?("Release.slnx")
errors << "corrected CI does not run tests" unless ci.match?(/dotnet test/)
errors << "corrected CI must preserve build revision across both builds" unless
  ci.scan(%r{/p:BuildRevision=\$\{\{ github\.sha \}\}}).length == 2
errors << "corrected CI must use .NET 10" unless ci.include?('dotnet-version: "10.0.x"')
errors << "corrected CI needs read-only contents permission" unless ci.match?(/permissions:\s*\n\s+contents: read/)

deploy_permissions = deploy[/permissions:\s*\n((?:\s{2}.+\n)+)/, 1].to_s
errors << "OIDC workflow permissions are not the minimal verification and deployment set" unless
  deploy_permissions.lines.map(&:strip).reject(&:empty?).sort ==
  ["attestations: read", "contents: read", "id-token: write", "packages: read"]
errors << "OIDC workflow must use the pinned azure/login v2 reference" unless
  deploy.include?("uses: azure/login@7184910d9eb2b1c5e48f7073824a90609bb9b6d6")
errors << "OIDC workflow must use GitHub variables" unless deploy.scan(/\$\{\{\s*vars\./).length >= 5
errors << "OIDC workflow must target the production environment" unless deploy.match?(/environment:\s*\n\s+name: production/)
errors << "OIDC workflow must have concurrency" unless deploy.include?("concurrency:")
errors << "OIDC workflow must have a timeout" unless deploy.include?("timeout-minutes:")
errors << "OIDC workflow must only update an existing Container App" unless deploy.include?("az containerapp update")
errors << "deployment must bind the approved digest to the repository release image subject" unless
  deploy.include?('IMAGE_SUBJECT: ghcr.io/${{ github.repository }}/release-api') &&
  deploy.include?('--image "$IMAGE_SUBJECT@$IMAGE_DIGEST"')
errors << "deployment must verify provenance and SBOM before Azure login" unless
  deploy.scan(/gh attestation verify/).length == 2 &&
  deploy.include?('--signer-workflow "$signer_workflow"') &&
  deploy.include?('--predicate-type "https://spdx.dev/Document"') &&
  deploy.include?("--deny-self-hosted-runners") &&
  deploy.index("gh attestation verify") < deploy.index("uses: azure/login@")
errors << "AZURE_CREDENTIALS is forbidden" if all.match?(/AZURE_CREDENTIALS/i)

forbidden = /
  az\s+(group|ad|role)\s+(create|assignment|federated-credential) |
  az\s+containerapp\s+(create|env\s+create) |
  az\s+acr\s+create |
  azd\s+(up|provision) |
  terraform\s+apply |
  bicep\s+build |
  az\s+deployment
/ix
errors << "workflow contains a provisioning command" if all.match?(forbidden)

errors << "dependency review action missing" unless
  dependency.include?("actions/dependency-review-action@2031cfc080254a8a887f58cffee85186f0e49e48")
errors << "dependency review severity gate missing" unless dependency.include?("fail-on-severity: high")
errors << "CodeQL init/analyze missing" unless
  codeql.include?("github/codeql-action/init@db488ddef3bf6cb639b32c2e9a7c0a7ea8271d28") &&
  codeql.include?("github/codeql-action/analyze@db488ddef3bf6cb639b32c2e9a7c0a7ea8271d28")
%w[
  docker/login-action@c94ce9fb468520275223c153574b00df6fe4bcc9
  docker/build-push-action@10e90e3645eae34f1e60eeb005ba3a3d33f178e8
  anchore/sbom-action@e22c389904149dbc22b58101806040fa8d37a610
  actions/attest-sbom@4651f806c01d8637787e274ac3bdf724ef169f34
  actions/attest-build-provenance@977bb373ede98d70efdf65b84cb5f73e068dcc2a
].each do |reference|
  errors << "supply-chain action missing: #{reference}" unless supply.include?(reference)
end
%w[contents: packages: id-token: attestations:].each do |permission|
  errors << "supply-chain permission missing: #{permission}" unless supply.include?(permission)
end

action_references = all.scan(/uses:\s*([^\s#]+)/).flatten
errors << "no action references found" if action_references.empty?
approved_references = %w[
  actions/checkout@11d5960a326750d5838078e36cf38b85af677262
  actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1
  azure/login@7184910d9eb2b1c5e48f7073824a90609bb9b6d6
  actions/dependency-review-action@2031cfc080254a8a887f58cffee85186f0e49e48
  github/codeql-action/init@db488ddef3bf6cb639b32c2e9a7c0a7ea8271d28
  github/codeql-action/analyze@db488ddef3bf6cb639b32c2e9a7c0a7ea8271d28
  docker/login-action@c94ce9fb468520275223c153574b00df6fe4bcc9
  docker/setup-buildx-action@8d2750c68a42422c14e847fe6c8ac0403b4cbd6f
  docker/build-push-action@10e90e3645eae34f1e60eeb005ba3a3d33f178e8
  anchore/sbom-action@e22c389904149dbc22b58101806040fa8d37a610
  actions/attest-sbom@4651f806c01d8637787e274ac3bdf724ef169f34
  actions/attest-build-provenance@977bb373ede98d70efdf65b84cb5f73e068dcc2a
]
action_references.each do |reference|
  errors << "unapproved or unpinned action reference: #{reference}" unless
    reference.match?(%r{\A[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+(?:/[A-Za-z0-9_.-]+)?@[0-9a-f]{40}\z}) &&
    approved_references.include?(reference)
end

if errors.any?
  warn errors.map { |error| "ERROR: #{error}" }.join("\n")
  exit 1
end

puts "PASS: parsed #{required.length} workflow fixtures"
puts "PASS: challenge defect is present and corrected CI properties are enforced"
puts "PASS: attestations, OIDC, least permissions, production gate, and update-only Azure scope are enforced"
puts "PASS: dependency review, CodeQL, GHCR, SBOM, and attestations are referenced"
puts "PASS: #{action_references.length} action references use approved immutable commits"
RUBY
