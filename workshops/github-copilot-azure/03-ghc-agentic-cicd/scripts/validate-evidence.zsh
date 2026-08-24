#!/bin/zsh
emulate -LR zsh
setopt errexit nounset pipefail

readonly ROOT="${0:A:h:h}"
readonly MANIFEST="${1:-$ROOT/fixtures/evidence/sample/evidence-manifest.json}"
readonly SUMMARY="${2:-$ROOT/fixtures/evidence/sample/release-summary.md}"
export WORKSHOP_ROOT="$ROOT" EVIDENCE_MANIFEST="$MANIFEST" RELEASE_SUMMARY="$SUMMARY"

ruby <<'RUBY'
require "digest"
require "json"
require "pathname"

root = Pathname.new(ENV.fetch("WORKSHOP_ROOT")).realpath
manifest_path = Pathname.new(ENV.fetch("EVIDENCE_MANIFEST")).realpath
summary_path = Pathname.new(ENV.fetch("RELEASE_SUMMARY")).realpath
manifest = JSON.parse(manifest_path.read)
summary = summary_path.read
errors = []

errors << "manifest kind must identify sample or generated evidence" unless
  %w[sample-evidence-manifest generated-evidence-manifest].include?(manifest["kind"])
errors << "sample marker and manifest kind disagree" unless
  manifest["sample"] == manifest["kind"].start_with?("sample-")
errors << "summary sample marker does not match manifest" unless
  summary.include?(manifest["sample"] ? "SAMPLE EVIDENCE" : "GENERATED EVIDENCE")

entries = Array(manifest["evidence"])
ids = entries.map { |entry| entry["id"] }
errors << "evidence IDs must be unique" unless ids.uniq.length == ids.length
by_id = entries.to_h { |entry| [entry["id"], entry] }
claim_lines = summary.lines.grep(/\A- \*\*/)
errors << "summary has no evidence-backed claims" if claim_lines.empty?

claim_lines.each do |line|
  citations = line.scan(/\[EVIDENCE:([a-z0-9-]+)\]/).flatten
  errors << "claim lacks exactly one evidence citation: #{line.strip}" unless citations.length == 1
  errors << "claim references unknown evidence: #{citations.first}" if citations.length == 1 && !by_id.key?(citations.first)
end

entries.each do |entry|
  id = entry.fetch("id", "")
  relative = entry.fetch("path", "")
  errors << "invalid evidence id: #{id}" unless id.match?(/\A[a-z0-9-]+\z/)
  path = root.join(relative).cleanpath
  errors << "evidence escapes workshop root: #{relative}" unless path.to_s.start_with?("#{root}/")
  expected_dir = root.join(
    manifest["sample"] ? "fixtures/evidence/sample" : "fixtures/evidence/generated"
  ).realpath
  errors << "#{manifest["kind"]} cannot reference #{relative}" unless
    path.to_s.start_with?("#{expected_dir}/")
  unless path.file?
    errors << "missing evidence file: #{relative}"
    next
  end
  errors << "#{manifest["kind"]} cannot follow #{relative} outside its evidence directory" unless
    path.realpath.to_s.start_with?("#{expected_dir}/")
  expected = entry.fetch("sha256", "")
  actual = Digest::SHA256.file(path).hexdigest
  errors << "hash mismatch for #{id}" unless expected == actual
  errors << "green evidence #{id} is never cited" if %w[passed approved published].include?(entry["status"]) &&
    !summary.include?("[EVIDENCE:#{id}]")
end

if errors.any?
  warn errors.map { |error| "ERROR: #{error}" }.join("\n")
  exit 1
end

puts "PASS: #{claim_lines.length} claims each cite one manifest entry"
puts "PASS: #{entries.length} evidence files exist and match SHA-256"
puts "PASS: sample/generated evidence markers agree"
RUBY
