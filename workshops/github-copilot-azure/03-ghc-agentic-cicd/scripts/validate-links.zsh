#!/bin/zsh
emulate -LR zsh
setopt errexit nounset pipefail

readonly ROOT="${0:A:h:h}"
export WORKSHOP_ROOT="$ROOT"

ruby <<'RUBY'
require "pathname"

root = Pathname.new(ENV.fetch("WORKSHOP_ROOT")).realpath
errors = []
Dir.glob(root.join("**", "*.md")).sort.each do |file|
  text = File.read(file)
  text.scan(/!?\[[^\]]*\]\(([^)]+)\)/).flatten.each do |target|
    target = target.split(/\s+["']/, 2).first
    next if target.match?(/\A(?:https?:|mailto:|#)/)
    relative = target.split("#", 2).first
    next if relative.empty?
    resolved = Pathname.new(File.dirname(file)).join(relative).cleanpath
    errors << "#{Pathname.new(file).relative_path_from(root)} -> #{target}" unless resolved.exist?
  end
end

if errors.any?
  warn "ERROR: broken local links:\n#{errors.join("\n")}"
  exit 1
end
puts "PASS: all local Markdown links resolve inside Month 3"
RUBY
