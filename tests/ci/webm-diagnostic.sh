#!/usr/bin/env bash
# CI diagnostic for the WebM cut (see WebmCutDiagnostic.cs). Decodes every cut-<label>.webm with every ffmpeg in
# VIDEOPREP_DIAG_FFMPEGS ("label=folder;label=folder") and prints, as GitHub annotations (readable without a login):
# the ffmpeg versions, the decoding messages (decoder and demuxer-only), the audio stream data and the audio packets.
# usage: webm-diagnostic.sh <folder with the cut files>
dir="${1:?folder}"
declare -A bin
labels=()
IFS=';' read -ra items <<< "${VIDEOPREP_DIAG_FFMPEGS:-}"
for it in "${items[@]}"; do labels+=("${it%%=*}"); bin["${it%%=*}"]="${it#*=}"; done

# GitHub annotation text: "%", carriage returns and line breaks must be written as %25, %0D and %0A.
esc() { sed -e ':a' -e 'N' -e '$!ba' -e 's/%/%25/g' -e 's/\r/%0D/g' -e 's/\n/%0A/g'; }
note() { local title="$1" body; body=$(esc); echo "::notice title=${title}::${body}"; }

{
  for l in "${labels[@]}"; do
    d="${bin[$l]}"
    echo "== ${l}: ${d}"
    "$d/ffmpeg" -version | head -2
    "$d/ffprobe" -version | head -1
    "$d/ffmpeg" -hide_banner -decoders 2>/dev/null | grep -iE ' opus| libopus'
    "$d/ffmpeg" -hide_banner -encoders 2>/dev/null | grep -iE ' opus| libopus'
  done
} 2>&1 | note "WEBM1 ffmpeg builds"

rm -f "$dir/summary.txt"
for producer in "${labels[@]}"; do
  f="$dir/cut-${producer}.webm"
  {
    echo "cut made by: ${producer}"
    echo "args: $(cat "$dir/cut-${producer}.args.txt" 2>/dev/null)"
    ls -l "$f" 2>&1
    for decoder in "${labels[@]}"; do
      d="${bin[$decoder]}"
      # Same level as the strict test (-v error): any line printed here would fail that test.
      out=$("$d/ffmpeg" -hide_banner -v error -nostats -i "$f" -map 0:a:0 -f null - 2>&1); code=$?
      n=$(printf '%s' "$out" | grep -c .)
      echo "-- DECODED by ${decoder} (-v error, like the test): exit=${code} messages=${n}"
      printf '%s\n' "$out" | head -6
      outc=$("$d/ffmpeg" -hide_banner -v error -nostats -i "$f" -map 0:a:0 -c copy -f null - 2>&1); codec=$?
      nc=$(printf '%s' "$outc" | grep -c .)
      echo "-- READ without decoding (-c copy) by ${decoder}: exit=${codec} messages=${nc}"
      printf '%s\n' "$outc" | head -4
      # Warnings too (a harmless "Discarding stream metadata" line of newer builds is left out).
      outw=$("$d/ffmpeg" -hide_banner -v warning -nostats -i "$f" -map 0:a:0 -f null - 2>&1 | grep -v "Discarding stream metadata")
      echo "-- warnings by ${decoder}: $(printf '%s' "$outw" | grep -c .)"
      printf '%s\n' "$outw" | head -4
      echo "made by ${producer}, decoded by ${decoder}: messages=${n} (without decoding: ${nc})" >> "$dir/summary.txt"
    done
  } 2>&1 | note "WEBM2 decode - cut made by ${producer}"

  {
    echo "audio stream data of the cut made by ${producer}"
    for pr in "${labels[@]}"; do
      echo "-- ffprobe of ${pr}"
      "${bin[$pr]}/ffprobe" -v error -select_streams a -show_streams -of default=nw=1 "$f" 2>&1 \
        | grep -E '^(codec_name|profile|sample_rate|channels|channel_layout|bit_rate|start_time|duration|duration_ts|nb_frames|nb_read_frames|extradata_size|time_base|start_pts|initial_padding|TAG:|DISPOSITION:default)'
    done
    p="${bin[${labels[0]}]}/ffprobe"
    echo "-- audio packets (pts_time,duration_time,size)"
    all=$("$p" -v error -select_streams a:0 -show_entries packet=pts_time,duration_time,size -of csv=p=0 "$f" 2>&1)
    echo "packets: $(printf '%s\n' "$all" | grep -c .)"
    echo "first 3:"; printf '%s\n' "$all" | head -3
    echo "last 4:";  printf '%s\n' "$all" | tail -4
    echo "smallest 3 by size:"; printf '%s\n' "$all" | sort -t, -k3 -n | head -3
    echo "-- video stream end for comparison"
    "$p" -v error -select_streams v:0 -show_entries stream=duration,nb_read_packets -count_packets -of default=nw=1 "$f" 2>&1 | head -4
  } 2>&1 | note "WEBM3 audio data - cut made by ${producer}"
done

note "WEBM0 summary" < "$dir/summary.txt"
exit 0
