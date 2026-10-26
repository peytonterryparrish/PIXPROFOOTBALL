#!/usr/bin/perl
# v0.13.0 STANCE-HEAD: stance compliance per position from MatchWatchAudit's stance_snapshots.csv (the last pre-snap sample of every body at the snap).
# usage: perl stance_compliance.pl stance_snapshots.csv [scrimmage|all] [-v]
# A snapshot COMPLIES when all of these hold: (1) stance type allowed for the position, (2) trunk lean and hips height inside the band of the stance family (depth),
# (3) facing within 20 deg of the simulated facing (28 for the tilted wide-9 edge), (4) hand down for the hand-down stances (lowest hand <= 0.18 m, four-point both <= 0.25),
# (5) lowest toe bone between -0.06 and +0.07 m of the turf. QB rows are listed but owned by the QB-MOCAP track.
use strict; use warnings;
my ($file, $which, $verbose) = ($ARGV[0], $ARGV[1] // 'scrimmage', ($ARGV[2] // '') eq '-v');
open(my $fh, '<', $file) or die "open $file: $!"; my $hdr = <$fh>;
my %allowed = (
  C  => [qw(CenterSnap)], OL => [qw(ThreePoint FourPoint OlTwoPoint)], DL => [qw(ThreePoint FourPoint TwoPointLine)],
  LB => [qw(LinebackerTwoPoint LinebackerBlitz)], CB => [qw(CornerPress CornerOff)], S => [qw(SafetyDeep)],
  WR => [qw(ReceiverSplit ReceiverSlot)], TE => [qw(ThreePoint TwoPointLine ReceiverSplit ReceiverSlot)],
  RB => [qw(BackTwoPoint)], FB => [qw(BackTwoPoint ThreePoint)], QB => [qw(QbShotgun QbUnderCenter)],
);
my %down = map { $_ => 1 } qw(ThreePoint FourPoint CenterSnap);
my (%n, %ok, %why);
while (<$fh>) {
  chomp; my @c = split /,/;
  my ($play,$kind,$ag,$pos,$grp,$st,$hips,$headY,$hl,$hr,$toe,$lean,$yaw,$stag,$wid,$hmh) = @c[0..15];
  next if $which eq 'scrimmage' && $kind ne 'Scrimmage';
  my $key = $pos eq 'C' ? 'C' : $grp; my @why;
  my $okk = 0; for my $a (@{ $allowed{$key} // [] }) { $okk = 1 if $a eq $st } $okk = 1 if $kind ne "Scrimmage" && $st =~ /^(Kicker|Punter|LongSnapper|Holder|KickoffRunner|KickoffReceive|Returner|Gunner|Upright)$/; push @why, "type" unless $okk;
  my $lo = $hl < $hr ? $hl : $hr; my $hi = $hl > $hr ? $hl : $hr;
  if ($down{$st}) {
    push @why, "lean" if $lean < 55 || $lean > 110; push @why, "hips" if $hips < 0.55 || $hips > 0.95;
    push @why, "hand" if $lo > 0.18; push @why, "hand2" if $st eq 'FourPoint' && $hi > 0.25;
    push @why, "head" if $hmh < -0.05 && $st ne 'CenterSnap';
  } elsif ($st eq 'QbUnderCenter') { push @why, "lean" if $lean < 15 || $lean > 55; push @why, "hips" if $hips < 0.5 || $hips > 0.95; }
  elsif ($st eq 'CornerPress' || $st eq 'QbShotgun') { push @why, "lean" if $lean < 2 || $lean > 32; push @why, "hips" if $hips < 0.7 || $hips > 1.05; }
  elsif ($st eq 'Kicker' || $st eq 'Punter' || $st eq 'Returner' || $st eq 'KickoffRunner' || $st eq 'KickoffReceive' || $st eq 'Upright') { push @why, "lean" if $lean > 25; push @why, "hips" if $hips < 0.75 || $hips > 1.08; }
  elsif ($st eq 'Holder' || $st eq 'LongSnapper') { }
  else { push @why, "lean" if $lean < 5 || $lean > 42; push @why, "hips" if $hips < 0.62 || $hips > 1.02; push @why, "head" if $hmh < 0.25; }
  my $ytol = $st eq 'TwoPointLine' ? 28 : 20; push @why, "face" if $yaw > $ytol;
  push @why, "feet" if $toe > 0.07 || $toe < -0.06;
  my %seen; for my $k (grep { !$seen{$_}++ } ($key, "ALL", $pos)) { $n{$k}++; if (@why) { $why{$k}{$_}++ for @why } else { $ok{$k}++ } }
}
printf "%-6s %6s %7s  %s\n", "pos", "n", "comply%", "failures";
for my $k (sort { ($a eq 'ALL') <=> ($b eq 'ALL') || $a cmp $b } keys %n) {
  next if !$verbose && $k !~ /^(C|OL|DL|LB|CB|S|WR|TE|RB|FB|QB|ALL)$/;
  printf "%-6s %6d %6.1f%%  %s\n", $k, $n{$k}, 100*($ok{$k}//0)/$n{$k}, join(' ', map { "$_=$why{$k}{$_}" } sort keys %{ $why{$k} // {} });
}
