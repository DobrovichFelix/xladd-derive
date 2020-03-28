use rust_xl::variant::Variant;
use rust_xl::xlcall::LPXLOPER12;
use xlmacro::*;

/// This normalizes a set of values
/// * arg - Takes a floating point number
/// * foo - Takes an array of values
/// * bar - Takes a string
/// * ret - Returns an array
#[xl_func(volatile, threadsafe)]
fn normalize(
    arg: f64,
    foo: &[f64],
    bar: &str,
) -> Result<(Vec<f64>, usize), Box<dyn std::error::Error>> {
    Ok((vec![], 2))
}

fn main() {
    let reg = Reg::new();
    register_normalize(&reg);
}
