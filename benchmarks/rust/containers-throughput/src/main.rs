use hashbrown::HashMap as FastHashMap;
use std::collections::HashMap as StdHashMap;
use std::env;
use std::hint::black_box;
use std::time::Instant;

const ARRAY_N: i64 = 50_000_000;
const ARRAY_SCAN_REPEATS: i64 = 20;
const DICT_N: i64 = 8_000_000;

fn main() {
    let mode = env::args().nth(1).unwrap_or_else(|| "std".to_string());
    match mode.as_str() {
        "std" => run_std_hash_map(),
        "hashbrown" => run_hashbrown_hash_map(),
        _ => panic!("unknown mode: use 'std' or 'hashbrown'"),
    }
}

fn run_std_hash_map() {
    warmup_std();
    let array = measure_array();

    let dict_build_start = Instant::now();
    let mut scores = StdHashMap::<i64, i64>::new();
    scores.insert(0, 0);
    for i in 1..=DICT_N {
        scores.insert(i, i * 3);
    }
    let dict_build_millis = elapsed_millis(dict_build_start);

    let dict_lookup_start = Instant::now();
    let mut dict_checksum = 0i64;
    for i in 1..=DICT_N {
        dict_checksum += scores[&i];
    }
    let dict_lookup_millis = elapsed_millis(dict_lookup_start);

    print_result(
        "rust-std",
        array,
        scores.len(),
        scores.capacity(),
        dict_checksum,
        dict_build_millis,
        dict_lookup_millis,
    );

    black_box(scores);
}

fn run_hashbrown_hash_map() {
    warmup_hashbrown();
    let array = measure_array();

    let dict_build_start = Instant::now();
    let mut scores = FastHashMap::<i64, i64>::new();
    scores.insert(0, 0);
    for i in 1..=DICT_N {
        scores.insert(i, i * 3);
    }
    let dict_build_millis = elapsed_millis(dict_build_start);

    let dict_lookup_start = Instant::now();
    let mut dict_checksum = 0i64;
    for i in 1..=DICT_N {
        dict_checksum += scores[&i];
    }
    let dict_lookup_millis = elapsed_millis(dict_lookup_start);

    print_result(
        "rust-hashbrown",
        array,
        scores.len(),
        scores.capacity(),
        dict_checksum,
        dict_build_millis,
        dict_lookup_millis,
    );

    black_box(scores);
}

fn measure_array() -> ArrayMetrics {
    let array_build_start = Instant::now();
    let mut values = Vec::<i64>::new();
    for i in 1..=ARRAY_N {
        values.push(i);
    }
    let array_build_millis = elapsed_millis(array_build_start);

    let array_scan_start = Instant::now();
    let mut array_checksum = 0i64;
    for _ in 1..=ARRAY_SCAN_REPEATS {
        let mut scan_checksum = 0i64;
        for value in &values {
            scan_checksum += *value;
        }
        array_checksum += scan_checksum;
    }
    let array_scan_millis = elapsed_millis(array_scan_start);

    let metrics = ArrayMetrics {
        length: values.len(),
        capacity: values.capacity(),
        backing_bytes: values.capacity() as i64 * 8,
        checksum: array_checksum,
        build_millis: array_build_millis,
        scan_millis: array_scan_millis,
    };

    black_box(values);
    metrics
}

fn print_result(
    language: &str,
    array: ArrayMetrics,
    dict_length: usize,
    dict_capacity: usize,
    dict_checksum: i64,
    dict_build_millis: i64,
    dict_lookup_millis: i64,
) {
    let array_scan_operations = ARRAY_N * ARRAY_SCAN_REPEATS;

    println!("benchmark = containers-throughput");
    println!("language = {language}");
    println!("arrayN = {ARRAY_N}");
    println!("arrayScanRepeats = {ARRAY_SCAN_REPEATS}");
    println!("dictN = {DICT_N}");
    println!("arrayLength = {}", array.length);
    println!("arrayCapacity = {}", array.capacity);
    println!("arrayBackingBytes = {}", array.backing_bytes);
    println!("arrayChecksum = {}", array.checksum);
    println!("arrayBuildMillis = {}", array.build_millis);
    println!(
        "arrayBuildOpsPerSecond = {}",
        ops_per_second(ARRAY_N, array.build_millis)
    );
    println!("arrayBuildAllocatedBytes = 0");
    println!("arrayScanMillis = {}", array.scan_millis);
    println!(
        "arrayScanOpsPerSecond = {}",
        ops_per_second(array_scan_operations, array.scan_millis)
    );
    println!("arrayScanAllocatedBytes = 0");
    println!("dictLength = {dict_length}");
    println!("dictCapacity = {dict_capacity}");
    println!("dictBackingBytes = 0");
    println!("dictChecksum = {dict_checksum}");
    println!("dictBuildMillis = {dict_build_millis}");
    println!(
        "dictBuildOpsPerSecond = {}",
        ops_per_second(DICT_N, dict_build_millis)
    );
    println!("dictBuildAllocatedBytes = 0");
    println!("dictLookupMillis = {dict_lookup_millis}");
    println!(
        "dictLookupOpsPerSecond = {}",
        ops_per_second(DICT_N, dict_lookup_millis)
    );
    println!("dictLookupAllocatedBytes = 0");
}

fn elapsed_millis(start: Instant) -> i64 {
    start.elapsed().as_millis() as i64
}

fn ops_per_second(operations: i64, millis: i64) -> i64 {
    if millis > 0 {
        operations * 1000 / millis
    } else {
        0
    }
}

fn warmup_std() {
    let mut scores = StdHashMap::<i64, i64>::new();
    warmup_map_insert_lookup(&mut scores);
}

fn warmup_hashbrown() {
    let mut scores = FastHashMap::<i64, i64>::new();
    warmup_map_insert_lookup(&mut scores);
}

fn warmup_map_insert_lookup<M>(scores: &mut M)
where
    M: InsertLookup,
{
    let mut values = Vec::<i64>::new();
    for i in 1..=1024 {
        values.push(i);
    }

    let mut checksum = 0i64;
    for value in &values {
        checksum += *value;
    }

    scores.insert_value(0, 0);
    for i in 1..=1024 {
        scores.insert_value(i, i * 3);
    }

    for i in 1..=1024 {
        checksum += scores.lookup_value(i);
    }

    black_box(checksum);
}

trait InsertLookup {
    fn insert_value(&mut self, key: i64, value: i64);
    fn lookup_value(&self, key: i64) -> i64;
}

impl InsertLookup for StdHashMap<i64, i64> {
    fn insert_value(&mut self, key: i64, value: i64) {
        self.insert(key, value);
    }

    fn lookup_value(&self, key: i64) -> i64 {
        self[&key]
    }
}

impl InsertLookup for FastHashMap<i64, i64> {
    fn insert_value(&mut self, key: i64, value: i64) {
        self.insert(key, value);
    }

    fn lookup_value(&self, key: i64) -> i64 {
        self[&key]
    }
}

#[derive(Clone, Copy)]
struct ArrayMetrics {
    length: usize,
    capacity: usize,
    backing_bytes: i64,
    checksum: i64,
    build_millis: i64,
    scan_millis: i64,
}
